using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Qa.Models;
using Sora.Core.Types;
using Sora.Entities.Info;

namespace Shinano.Plugin.Qa.Endpoints;

/// <summary>有人问专有面板的管理 API 处理方法；路由由 <see cref="QaPanelApi" /> 登记。错误响应为 <c>{ error }</c>。</summary>
internal static class QaPanelEndpoints
{
    private const string FIELD_GROUP    = "groupId";
    private const string FIELD_TYPE     = "matchType";
    private const string FIELD_QUESTION = "question";
    private const string FIELD_PARTS    = "parts";
    private const string FIELD_REPLACE  = "replaceVersion";
    private const string IMAGE_PREFIX   = "image/";
    private const string AUDIO_PREFIX   = "audio/";
    private const string VIDEO_PREFIX   = "video/";
    private const string DUPLICATE      = "duplicate";
    private const string CHANGED        = "changed";

    private static readonly JsonSerializerOptions PartsJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Bot 当前所在的群；未连接时为空。</summary>
    internal static async Task<IResult> GroupsAsync(IPluginInitializationContext context)
    {
        if (context.Bot.Api is not { } api) return Results.Ok(Array.Empty<QaGroupView>());
        ApiResult<IReadOnlyList<GroupInfo>> groups = await api.GetGroupListAsync();
        return Results.Ok(groups is { IsSuccess: true, Data: { } list }
            ? list.Select(group => new QaGroupView
                  {
                      GroupId = group.GroupId,
                      Name    = group.GroupName
                  })
                  .OrderBy(group => group.Name, StringComparer.CurrentCulture)
                  .ToArray()
            : []);
    }

    /// <summary>按群、匹配方式与问题关键字列出词条，越新越靠前。</summary>
    internal static async Task<IResult> ListAsync(QaStore store, long? groupId, QaMatchType? matchType, string? keyword) =>
        Results.Ok((await store.QueryAsync(groupId, matchType, keyword)).Select(QaEntryView.From));

    /// <summary>删除词条及其媒体引用。</summary>
    internal static async Task<IResult> DeleteAsync(QaStore store, QaMediaStore media, ILogger logger, long id)
    {
        if (await store.DeleteByIdAsync(id) is not { } deleted) return Results.NotFound();
        await media.RemoveAsync(deleted.MediaKey);
        logger.LogInformation("面板删除有人问：群 {GroupId}，词条 {EntryId}", deleted.GroupId, deleted.Id);
        return Results.NoContent();
    }

    /// <summary>读取词条中的媒体；只允许读取当前账号词条使用的媒体。</summary>
    internal static async Task<IResult> MediaAsync(QaStore store, IPluginInitializationContext context, long id, CancellationToken cancellationToken)
    {
        if (!await store.UsesMediaAsync(id)) return Results.NotFound();
        Stream? stream = await context.Media.OpenReadAsync(id, cancellationToken);
        return stream is null ? Results.NotFound() : Results.Stream(stream, QaMediaStore.UNKNOWN_MEDIA_TYPE);
    }

    /// <summary>
    /// 新增词条（multipart 表单）：<c>groupId</c>、<c>matchType</c>、<c>question</c>、<c>parts</c>（<see cref="QaUploadPart" /> 数组的 JSON）
    /// 与各上传文件。问题已存在时返回 409 <c>{ error: "duplicate", existing }</c>；带上 <c>replaceVersion</c> 再次提交即覆盖，
    /// 期间被他人修改时返回 409 <c>{ error: "changed" }</c>。
    /// </summary>
    internal static async Task<IResult> AddAsync(
        HttpRequest                  request,
        QaStore                      store,
        QaMediaStore                 media,
        IPluginInitializationContext context,
        ILogger                      logger,
        CancellationToken            cancellationToken)
    {
        IFormCollection form = await request.ReadFormAsync(cancellationToken);
        if (!long.TryParse(form[FIELD_GROUP], out long groupId) || groupId <= 0) return Error("请选择群");
        if (!Enum.TryParse(form[FIELD_TYPE], out QaMatchType matchType)) return Error("匹配方式无效");
        string question = form[FIELD_QUESTION].ToString().Trim();
        if (question.Length == 0) return Error("问题不能为空");
        if (await store.RejectQuestionAsync(question, matchType) is { } rejection) return Error(rejection);

        List<QaUploadPart>? parts;
        try
        {
            parts = JsonSerializer.Deserialize<List<QaUploadPart>>(form[FIELD_PARTS].ToString(), PartsJson);
        }
        catch (JsonException)
        {
            parts = null;
        }

        if (parts is null || parts.Count == 0) return Error("回答不能为空");

        QaEntry? existing = await store.FindAsync(groupId, matchType, question);
        int? replaceVersion = int.TryParse(form[FIELD_REPLACE], out int version) ? version : null;
        if (existing is not null && replaceVersion != existing.Version)
            return Results.Conflict(new { error = DUPLICATE, existing = QaEntryView.From(existing) });

        string mediaKey = QaStore.NewMediaKey();
        List<QaAnswerPart> answer = [];
        try
        {
            foreach (QaUploadPart part in parts)
                if (await ToAnswerPartAsync(part, form.Files, context, mediaKey, cancellationToken) is { } built)
                    answer.Add(built);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or InvalidOperationException)
        {
            await media.RemoveAsync(mediaKey);
            logger.LogWarning(exception, "面板录入有人问：保存媒体失败");
            return Error($"媒体保存失败：{exception.Message}");
        }

        if (answer.Count == 0)
        {
            await media.RemoveAsync(mediaKey);
            return Error("回答不能为空");
        }

        if (existing is not null)
        {
            if (!await store.ReplaceAsync(existing, answer, mediaKey, createdBy: 0))
            {
                await media.RemoveAsync(mediaKey);
                return Results.Conflict(new { error = CHANGED });
            }

            await media.RemoveAsync(existing.MediaKey);
            logger.LogInformation("面板覆盖有人问：群 {GroupId}，词条 {EntryId}", groupId, existing.Id);
            return Results.Ok(QaEntryView.From((await store.FindAsync(groupId, matchType, question)) ?? existing));
        }

        if (await store.AddAsync(groupId, matchType, question, answer, mediaKey, createdBy: 0) is not { } added)
        {
            await media.RemoveAsync(mediaKey);
            return Results.Conflict(new { error = CHANGED });
        }

        logger.LogInformation("面板录入有人问：群 {GroupId}，词条 {EntryId}", groupId, added.Id);
        return Results.Ok(QaEntryView.From(added));
    }

    /// <summary>全部禁止提问词。</summary>
    internal static async Task<IResult> ForbiddenAsync(QaStore store) => Results.Ok(await store.ForbiddenAsync());

    /// <summary>新增禁止提问词。</summary>
    internal static async Task<IResult> AddForbiddenAsync(QaStore store, QaForbiddenRequest request)
    {
        string question = request.Question.Trim();
        if (question.Length == 0) return Error("内容不能为空");
        if (question.Length > QaRecordingParser.MAX_QUESTION_LENGTH) return Error($"最长 {QaRecordingParser.MAX_QUESTION_LENGTH} 个字");
        return await store.AddForbiddenAsync(question) ? Results.NoContent() : Error("已经有这一条了");
    }

    /// <summary>删除禁止提问词。</summary>
    internal static async Task<IResult> DeleteForbiddenAsync(QaStore store, long id) =>
        await store.DeleteForbiddenAsync(id) ? Results.NoContent() : Results.NotFound();

    private static async Task<QaAnswerPart?> ToAnswerPartAsync(
        QaUploadPart                 part,
        IFormFileCollection          files,
        IPluginInitializationContext context,
        string                       mediaKey,
        CancellationToken            cancellationToken)
    {
        switch (part.Type)
        {
            case QaUploadPartType.Text:
                return string.IsNullOrEmpty(part.Text)
                    ? null
                    : new QaAnswerPart
                    {
                        Kind = QaPartKind.Text,
                        Text = part.Text
                    };
            case QaUploadPartType.Mention:
                return part.UserId is > 0
                    ? new QaAnswerPart
                    {
                        Kind   = QaPartKind.Mention,
                        UserId = part.UserId
                    }
                    : throw new InvalidDataException("@ 的 QQ 号无效");
        }

        IFormFile file = files.GetFile(part.Field ?? string.Empty) ?? throw new InvalidDataException("缺少上传的文件");
        string contentType = string.IsNullOrWhiteSpace(file.ContentType) ? QaMediaStore.UNKNOWN_MEDIA_TYPE : file.ContentType;
        await using Stream content = file.OpenReadStream();
        long mediaId = await context.Media.StoreAsync(content, contentType, QaStore.RefKey(mediaKey), context.Database.SelfId, cancellationToken);
        QaPartKind kind = contentType.StartsWith(IMAGE_PREFIX, StringComparison.OrdinalIgnoreCase) ? QaPartKind.Image
            : contentType.StartsWith(AUDIO_PREFIX, StringComparison.OrdinalIgnoreCase) ? QaPartKind.Audio
            : contentType.StartsWith(VIDEO_PREFIX, StringComparison.OrdinalIgnoreCase) ? QaPartKind.Video
            : QaPartKind.File;
        return new QaAnswerPart
        {
            Kind     = kind,
            MediaId  = mediaId,
            FileName = kind == QaPartKind.File ? Path.GetFileName(file.FileName) : null
        };
    }

    private static IResult Error(string message) => Results.BadRequest(new { error = message });
}
