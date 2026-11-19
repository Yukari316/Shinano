using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Qa.Persistence;
using Sora.Core.Types;
using Sora.Entities.Interfaces;

namespace Shinano.Plugin.Qa;

/// <summary>
/// 把回答中的图片、语音、视频与文件保存为持久媒体，并以词条版本的媒体键登记引用。
/// 媒体内容不在录入时检查类型，统一以 <c>application/octet-stream</c> 保存，面板读取时再识别图片格式。
/// </summary>
public sealed class QaMediaStore
{
    /// <summary>聊天来源媒体的保存类型。</summary>
    public const string UNKNOWN_MEDIA_TYPE = "application/octet-stream";

    private readonly IPluginInitializationContext _context;
    private readonly ILogger<QaMediaStore>        _logger;

    /// <summary>创建媒体保存。</summary>
    public QaMediaStore(IPluginInitializationContext context, ILogger<QaMediaStore> logger)
    {
        _context = context;
        _logger  = logger;
    }

    /// <summary>保存草稿中的媒体，得到可存入词条的回答。</summary>
    /// <param name="api">机器人 API，用于重新取得临时下载地址与群文件下载地址。</param>
    /// <param name="groupId">录入所在的群。</param>
    /// <param name="drafts">回答草稿。</param>
    /// <param name="mediaKey">本版本的媒体键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>回答片段；任一媒体保存失败时为 null，已登记的引用会被删除，原因记入日志。</returns>
    public async Task<IReadOnlyList<QaAnswerPart>?> StoreAsync(
        IBotApi                    api,
        long                       groupId,
        IReadOnlyList<QaDraftPart> drafts,
        string                     mediaKey,
        CancellationToken          cancellationToken)
    {
        List<QaAnswerPart> parts = [];
        try
        {
            foreach (QaDraftPart draft in drafts)
                parts.Add(await ToPartAsync(api, groupId, draft, mediaKey, cancellationToken));
            return parts;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "保存有人问回答媒体失败（群 {GroupId}）", groupId);
            await RemoveAsync(mediaKey);
            return null;
        }
    }

    /// <summary>删除媒体键下的全部引用；媒体在最后一条引用删除后由宿主回收。</summary>
    public Task RemoveAsync(string mediaKey) => _context.Media.RemoveReferencesAsync(QaStore.RefKey(mediaKey));

    private async Task<QaAnswerPart> ToPartAsync(IBotApi api, long groupId, QaDraftPart draft, string mediaKey, CancellationToken cancellationToken)
    {
        switch (draft.Kind)
        {
            case QaPartKind.Text:
                return new QaAnswerPart
                {
                    Kind = QaPartKind.Text,
                    Text = draft.Text
                };
            case QaPartKind.Face:
                return new QaAnswerPart
                {
                    Kind      = QaPartKind.Face,
                    FaceId    = draft.FaceId,
                    LargeFace = draft.LargeFace
                };
            case QaPartKind.Mention:
                return new QaAnswerPart
                {
                    Kind   = QaPartKind.Mention,
                    UserId = draft.UserId
                };
        }

        string url = await ResolveUrlAsync(api, groupId, draft, cancellationToken);
        long mediaId = await _context.Media.StoreFromUrlAsync(
            new Uri(url),
            draft.DeclaredSize,
            UNKNOWN_MEDIA_TYPE,
            QaStore.RefKey(mediaKey),
            _context.Database.SelfId,
            cancellationToken);
        return new QaAnswerPart
        {
            Kind     = draft.Kind,
            MediaId  = mediaId,
            FileName = draft.FileName
        };
    }

    // 消息段自带的临时地址可能为空或已过期，按资源 ID 或群文件 ID 重新取得。
    private static async Task<string> ResolveUrlAsync(IBotApi api, long groupId, QaDraftPart draft, CancellationToken cancellationToken)
    {
        if (draft.Kind == QaPartKind.File)
        {
            ApiResult<string> file = await api.GetGroupFileDownloadUrlAsync(new GroupId(groupId), draft.FileId ?? string.Empty, cancellationToken);
            return file is { IsSuccess: true, Data: { Length: > 0 } fileUrl }
                ? fileUrl
                : throw new InvalidOperationException($"无法取得群文件下载地址：{file.Code} {file.Message}");
        }

        if (draft.Url is { Length: > 0 } url) return url;
        ApiResult<string> resource = await api.GetResourceTempUrlAsync(draft.ResourceId ?? string.Empty, cancellationToken);
        return resource is { IsSuccess: true, Data: { Length: > 0 } resourceUrl }
            ? resourceUrl
            : throw new InvalidOperationException($"无法取得媒体下载地址：{resource.Code} {resource.Message}");
    }
}
