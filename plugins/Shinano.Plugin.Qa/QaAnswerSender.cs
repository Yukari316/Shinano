using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Sora.Core.Types;
using Sora.Entities.Interfaces;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Plugin.Qa;

/// <summary>
/// 发送回答。QQ 不允许语音、视频与文字图片混在同一条消息，文件只能作为群文件，因此按原顺序拆开：
/// 相邻的文字、图片、表情与 @ 合为一条消息；每段语音、视频各自一条；文件以预签名直链上传为群文件。
/// 某一条失败时记录日志，其余照常发送，不在群里提示。
/// </summary>
public sealed class QaAnswerSender
{
    private readonly IPluginInitializationContext _context;
    private readonly ILogger<QaAnswerSender>      _logger;

    /// <summary>创建发送器。</summary>
    public QaAnswerSender(IPluginInitializationContext context, ILogger<QaAnswerSender> logger)
    {
        _context = context;
        _logger  = logger;
    }

    /// <summary>把回答拆为依次发送的单元：语音、视频、文件单独成组，其余相邻片段合为一组。</summary>
    public static IReadOnlyList<IReadOnlyList<QaAnswerPart>> Split(IReadOnlyList<QaAnswerPart> answer)
    {
        List<IReadOnlyList<QaAnswerPart>> units = [];
        List<QaAnswerPart> inline = [];
        foreach (QaAnswerPart part in answer)
        {
            if (!IsStandalone(part.Kind))
            {
                inline.Add(part);
                continue;
            }

            if (inline.Count > 0) units.Add([.. inline]);
            inline.Clear();
            units.Add([part]);
        }

        if (inline.Count > 0) units.Add(inline);
        return units;
    }

    /// <summary>在群里发送回答。</summary>
    public async Task SendAsync(IBotApi api, long groupId, IReadOnlyList<QaAnswerPart> answer)
    {
        foreach (IReadOnlyList<QaAnswerPart> unit in Split(answer))
        {
            try
            {
                if (unit is [{ Kind: QaPartKind.File } file]) await UploadFileAsync(api, groupId, file);
                else await SendMessageAsync(api, groupId, await BuildAsync(unit));
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "发送有人问回答失败（群 {GroupId}）", groupId);
            }
        }
    }

    /// <summary>覆盖确认中展示的已保存回答：文字、表情、@ 与图片原样，语音、视频、文件用占位文字。</summary>
    public async Task<MessageBody> PreviewAsync(IReadOnlyList<QaAnswerPart> answer)
    {
        MessageBody body = [];
        foreach (QaAnswerPart part in answer)
            body.Add(part.Kind switch
            {
                QaPartKind.Image => await ImUrlAsync(part) is { } url ? SegmentBuilder.Image(url) : SegmentBuilder.Text("[图片]"),
                QaPartKind.Audio or QaPartKind.Video or QaPartKind.File => SegmentBuilder.Text(Placeholder(part.Kind, part.FileName)),
                _ => Inline(part)
            });
        return body;
    }

    /// <summary>覆盖确认中展示的新回答（尚未保存）：文字、表情、@ 与图片沿用原消息段。</summary>
    public static MessageBody PreviewDraft(IReadOnlyList<QaDraftPart> drafts)
    {
        MessageBody body = [];
        foreach (QaDraftPart draft in drafts)
            body.Add(IsStandalone(draft.Kind) ? SegmentBuilder.Text(Placeholder(draft.Kind, draft.FileName)) : draft.Source);
        return body;
    }

    private static bool IsStandalone(QaPartKind kind) => kind is QaPartKind.Audio or QaPartKind.Video or QaPartKind.File;

    private static string Placeholder(QaPartKind kind, string? fileName) =>
        kind switch
        {
            QaPartKind.Audio => "[语音]",
            QaPartKind.Video => "[视频]",
            _                => $"[文件 {fileName}]"
        };

    private static Segment Inline(QaAnswerPart part) =>
        part.Kind switch
        {
            QaPartKind.Face    => SegmentBuilder.Face(part.FaceId ?? string.Empty, part.LargeFace),
            QaPartKind.Mention => SegmentBuilder.Mention(new UserId(part.UserId.GetValueOrDefault())),
            _                  => SegmentBuilder.Text(part.Text ?? string.Empty)
        };

    private async Task<MessageBody> BuildAsync(IReadOnlyList<QaAnswerPart> unit)
    {
        MessageBody body = [];
        foreach (QaAnswerPart part in unit)
        {
            switch (part.Kind)
            {
                case QaPartKind.Image or QaPartKind.Audio or QaPartKind.Video:
                    if (await ImUrlAsync(part) is not { } url)
                    {
                        _logger.LogWarning("有人问回答的媒体 {MediaId} 已不存在，跳过", part.MediaId);
                        continue;
                    }

                    body.Add(part.Kind switch
                    {
                        QaPartKind.Image => SegmentBuilder.Image(url),
                        QaPartKind.Audio => SegmentBuilder.Audio(url),
                        _                => SegmentBuilder.Video(url)
                    });
                    break;
                default:
                    body.Add(Inline(part));
                    break;
            }
        }

        return body;
    }

    private async Task SendMessageAsync(IBotApi api, long groupId, MessageBody body)
    {
        if (body.Count == 0) return;
        SendMessageResult result = await api.SendGroupMessageAsync(new GroupId(groupId), body);
        if (!result.IsSuccess)
            _logger.LogWarning("发送有人问回答失败（群 {GroupId}）：{Code} {Error}", groupId, result.Code, result.ErrorMessage);
    }

    private async Task UploadFileAsync(IBotApi api, long groupId, QaAnswerPart file)
    {
        if (await ImUrlAsync(file) is not { } url)
        {
            _logger.LogWarning("有人问回答的文件 {MediaId} 已不存在，跳过", file.MediaId);
            return;
        }

        ApiResult<string> result = await api.UploadGroupFileAsync(new GroupId(groupId), url, file.FileName ?? $"{file.MediaId}");
        if (!result.IsSuccess)
            _logger.LogWarning("上传有人问回答文件失败（群 {GroupId}）：{Code} {Message}", groupId, result.Code, result.Message);
    }

    private async Task<string?> ImUrlAsync(QaAnswerPart part) =>
        part.MediaId is { } mediaId ? await _context.Media.CreateImUrlAsync(mediaId) : null;
}
