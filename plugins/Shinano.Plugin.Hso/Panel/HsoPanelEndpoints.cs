using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Hso.Gallery;
using Shinano.Plugin.Hso.Info;
using Shinano.Plugin.Hso.Persistence;
using Sora.Core.Types;
using Sora.Entities.Info;

namespace Shinano.Plugin.Hso.Panel;

/// <summary>Hso 专有面板 API 的处理方法，路由见 <see cref="HsoPanelApi" />。</summary>
internal static class HsoPanelEndpoints
{
    /// <summary>上传请求（含 zip）的大小上限，单位字节。</summary>
    internal const long UPLOAD_LIMIT = 2L * 1024 * 1024 * 1024;

    private const string ZIP_EXTENSION   = ".zip";
    private const string JPEG            = "image/jpeg";
    private const long   MAX_ENTRY_BYTES = 200L * 1024 * 1024;

    /// <summary>Bot 所在的群与已保存设置的群，按群名排列；Bot 已不在的群排在最后。</summary>
    internal static async Task<IResult> GroupsAsync(IPluginInitializationContext context, HsoGroups groups)
    {
        Dictionary<long, HsoGroupSettingRecord> settings = (await groups.SettingsAsync()).ToDictionary(setting => setting.GroupId);
        IReadOnlyList<GroupInfo> joined = context.Bot.Api is { } api && await api.GetGroupListAsync() is { IsSuccess: true, Data: { } list } ? list : [];
        IEnumerable<HsoGroupView> current = joined.Select(group => View(group.GroupId, group.GroupName, settings.GetValueOrDefault(group.GroupId)))
                                                  .OrderBy(group => group.Name, StringComparer.CurrentCulture);
        HashSet<long> joinedIds = [.. joined.Select(group => (long)group.GroupId)];
        IEnumerable<HsoGroupView> left = settings.Values.Where(setting => !joinedIds.Contains(setting.GroupId))
                                                 .OrderBy(setting => setting.GroupId)
                                                 .Select(setting => View(setting.GroupId, null, setting));
        return Results.Ok(current.Concat(left).ToArray());
    }

    /// <summary>保存群设置。</summary>
    internal static async Task<IResult> SaveGroupAsync(HsoGroups groups, long groupId, HsoGroupUpdate update)
    {
        if (groupId <= 0) return Results.BadRequest(new { error = "群号无效" });
        await groups.SaveSettingAsync(new HsoGroupSettingRecord
        {
            GroupId   = groupId,
            Blocked   = update.Blocked,
            AllowNsfw = update.AllowNsfw
        });
        return Results.NoContent();
    }

    /// <summary>分页列出图片。</summary>
    internal static async Task<IResult> ListAsync(HsoGallery gallery, int? page, bool? nsfw, bool? hasPid, bool? blocked, string? keyword) =>
        Results.Ok(await gallery.QueryAsync(new HsoImageQuery
        {
            Page    = Math.Max(1, page ?? 1),
            Nsfw    = nsfw,
            HasPid  = hasPid,
            Blocked = blocked,
            Keyword = keyword
        }));

    /// <summary>即时绘制缩略图。</summary>
    internal static async Task<IResult> ThumbnailAsync(HsoGallery gallery, long id, CancellationToken cancellationToken)
    {
        if (await gallery.FindAsync(id) is not { } record || await gallery.ReadAsync(record, cancellationToken) is not { } content)
            return Results.NotFound();
        return HsoThumbnails.Render(content) is { } thumbnail ? Results.File(thumbnail, JPEG) : Results.NotFound();
    }

    /// <summary>原图。</summary>
    internal static async Task<IResult> OriginalAsync(HsoGallery gallery, long id, CancellationToken cancellationToken)
    {
        if (await gallery.FindAsync(id) is not { } record) return Results.NotFound();
        Stream? stream = await gallery.Bucket.OpenReadAsync(record.ObjectKey, cancellationToken);
        return stream is null ? Results.NotFound() : Results.Stream(stream, record.MediaType);
    }

    /// <summary>
    /// 上传图片（multipart，可多个文件）：图片文件逐个导入，zip 中的每个图片条目逐个导入，其余条目记为不支持；
    /// 按文件名 <c>&lt;pid&gt;_p&lt;页号&gt;.&lt;扩展名&gt;</c> 解析 pid 与页号。返回每个文件的结果。
    /// </summary>
    internal static async Task<IResult> UploadAsync(HttpRequest request, HsoGallery gallery, ILogger logger, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType) return Results.BadRequest(new { error = "请上传文件" });
        // 默认的 multipart 上限为 128 MB；处理方法直接读取表单，路由元数据不作用于此，在读取前显式设定。
        request.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions { MultipartBodyLengthLimit = UPLOAD_LIMIT }));
        IFormCollection form = await request.ReadFormAsync(cancellationToken);
        List<HsoImportResult> results = [];
        foreach (IFormFile file in form.Files)
        {
            if (file.FileName.EndsWith(ZIP_EXTENSION, StringComparison.OrdinalIgnoreCase))
            {
                await using Stream stream = file.OpenReadStream();
                await ImportZipAsync(stream, gallery, results, cancellationToken);
                continue;
            }

            byte[] content = await ReadAsync(file.OpenReadStream(), file.Length, cancellationToken);
            results.Add(await ImportAsync(gallery, content, file.FileName, HsoImageOrigin.Upload, cancellationToken));
        }

        logger.LogInformation(
            "面板上传 Hso 图片：{Total} 个文件，导入 {Imported}，重复 {Duplicate}，不支持 {Unsupported}，失败 {Failed}",
            results.Count,
            results.Count(result => result.Outcome == HsoImportOutcome.Imported),
            results.Count(result => result.Outcome == HsoImportOutcome.Duplicate),
            results.Count(result => result.Outcome == HsoImportOutcome.Unsupported),
            results.Count(result => result.Outcome == HsoImportOutcome.Failed));
        return Results.Ok(results);
    }

    /// <summary>设置 NSFW（之后补全作品信息不再覆盖）。</summary>
    internal static async Task<IResult> SetNsfwAsync(HsoGallery gallery, long id, HsoNsfwUpdate update) =>
        await gallery.SetNsfwAsync(id, update.Nsfw) ? Results.NoContent() : Results.NotFound();

    /// <summary>清除被拦截标记。</summary>
    internal static async Task<IResult> ClearBlockedAsync(HsoGallery gallery, long id) =>
        await gallery.ClearBlockedAsync(id) ? Results.NoContent() : Results.NotFound();

    /// <summary>删除图片。</summary>
    internal static async Task<IResult> DeleteAsync(HsoGallery gallery, long id, CancellationToken cancellationToken) =>
        await gallery.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    /// <summary>审核队列中全部图片的 ID，先进入的在前。</summary>
    internal static async Task<IResult> ReviewIdsAsync(HsoReviews reviews) => Results.Ok(await reviews.IdsAsync());

    /// <summary>审核队列中一张图片的对比与作品信息。</summary>
    internal static async Task<IResult> ReviewAsync(HsoReviews reviews, long id, CancellationToken cancellationToken) =>
        await reviews.DetailAsync(id, cancellationToken) is { } view ? Results.Ok(view) : Results.NotFound();

    /// <summary>即时绘制查询获取的原图的缩略图。</summary>
    internal static async Task<IResult> CandidateThumbnailAsync(HsoReviews reviews, HsoGallery gallery, long id, CancellationToken cancellationToken)
    {
        if (await reviews.FindAsync(id) is not { ObjectKey: { } key }) return Results.NotFound();
        await using Stream? stream = await gallery.Bucket.OpenReadAsync(key, cancellationToken);
        if (stream is null) return Results.NotFound();
        using MemoryStream buffer = new();
        await stream.CopyToAsync(buffer, cancellationToken);
        return HsoThumbnails.Render(buffer.ToArray()) is { } thumbnail ? Results.File(thumbnail, JPEG) : Results.NotFound();
    }

    /// <summary>查询获取的原图。</summary>
    internal static async Task<IResult> CandidateAsync(HsoReviews reviews, HsoGallery gallery, long id, CancellationToken cancellationToken)
    {
        if (await reviews.FindAsync(id) is not { ObjectKey: { } key, MediaType: { } mediaType }) return Results.NotFound();
        Stream? stream = await gallery.Bucket.OpenReadAsync(key, cancellationToken);
        return stream is null ? Results.NotFound() : Results.Stream(stream, mediaType);
    }

    /// <summary>覆盖：用查询获取的原图替换图库图片，并确认作品信息。</summary>
    internal static async Task<IResult> ReplaceReviewAsync(HsoReviews reviews, long id, CancellationToken cancellationToken) =>
        ReviewResult(await reviews.ReplaceAsync(id, cancellationToken));

    /// <summary>只确认信息：保留图库图片。</summary>
    internal static async Task<IResult> AcceptReviewAsync(HsoReviews reviews, long id, CancellationToken cancellationToken) =>
        ReviewResult(await reviews.AcceptAsync(id, cancellationToken));

    /// <summary>否决：丢弃查到的作品信息，改用手动补全的信息（全部留空时记为查无信息）。</summary>
    internal static async Task<IResult> RejectReviewAsync(HsoReviews reviews, long id, HsoManualInfo manual, CancellationToken cancellationToken) =>
        ReviewResult(await reviews.RejectAsync(id, manual, cancellationToken));

    private static IResult ReviewResult(HsoReviewResult result) =>
        result.Outcome switch
        {
            HsoReviewOutcome.Done        => Results.NoContent(),
            HsoReviewOutcome.NotFound    => Results.NotFound(new { error = "这张图片已处理或已删除" }),
            HsoReviewOutcome.NoCandidate => Results.Conflict(new { error = "原图获取失败，没有可覆盖的原图" }),
            _                            => Results.Conflict(new { error = $"查询获取的原图与图库中的 #{result.DuplicateId} 相同，未覆盖" })
        };

    private static async Task ImportZipAsync(Stream stream, HsoGallery gallery, List<HsoImportResult> results, CancellationToken cancellationToken)
    {
        ZipArchive archive;
        try
        {
            archive = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        }
        catch (InvalidDataException)
        {
            results.Add(HsoImportResult.Of(null, HsoImportOutcome.Unsupported));
            return;
        }

        await using (archive)
        {
            foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => !entry.FullName.EndsWith('/')))
            {
                if (entry.Length > MAX_ENTRY_BYTES)
                {
                    results.Add(HsoImportResult.Of(entry.FullName, HsoImportOutcome.Failed));
                    continue;
                }

                byte[] content;
                await using (Stream entryStream = await entry.OpenAsync(cancellationToken))
                    content = await ReadAsync(entryStream, entry.Length, cancellationToken);
                results.Add(await ImportAsync(gallery, content, entry.FullName, HsoImageOrigin.Zip, cancellationToken));
            }
        }
    }

    private static Task<HsoImportResult> ImportAsync(HsoGallery gallery, byte[] content, string fileName, HsoImageOrigin origin, CancellationToken cancellationToken)
    {
        HsoPixivPage? pixiv = HsoImageFiles.ParsePixivName(fileName);
        return gallery.ImportAsync(content, fileName, origin, pixiv?.Pid, pixiv?.Page, cancellationToken);
    }

    private static async Task<byte[]> ReadAsync(Stream stream, long length, CancellationToken cancellationToken)
    {
        await using (stream)
        {
            using MemoryStream buffer = new(length > 0 && length <= MAX_ENTRY_BYTES ? (int)length : 0);
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
    }

    private static HsoGroupView View(long groupId, string? name, HsoGroupSettingRecord? setting) =>
        new()
        {
            GroupId   = groupId,
            Name      = name,
            Blocked   = setting?.Blocked ?? false,
            AllowNsfw = setting?.AllowNsfw ?? false
        };
}
