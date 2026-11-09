using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Bilibili.Models;
using Sora.Core.Types;
using Sora.Entities.Interfaces;
using Sora.Entities.Message;

namespace Shinano.Plugin.Bilibili;

/// <summary>
/// 向群发送动态与直播提醒。动态为一条合并转发：「&lt;UP名&gt;更新了喵」「一共&lt;n&gt;条动态喵」，之后每条动态一张卡片；
/// 动态内单张图为一个图片节点，多张图为一个嵌套的合并转发，视频另发封面原图与 BV 号。节点发送者为插件配置的 QQ 号与 UP 名，
/// 不使用 Bot 自己的身份。原图节点直接使用 B站图片地址，由 IM 端下载。
/// </summary>
public sealed class BiliPusher
{
    private const string BASE64_PREFIX = "base64://";

    private readonly IPluginInitializationContext _context;
    private readonly ILogger<BiliPusher>          _logger;

    /// <summary>创建发送器。</summary>
    public BiliPusher(IPluginInitializationContext context, ILogger<BiliPusher> logger)
    {
        _context = context;
        _logger  = logger;
    }

    /// <summary>把一个 UP 的若干条动态以一条合并转发发到群。</summary>
    /// <param name="groupId">群号。</param>
    /// <param name="upName">UP 名，用作开头文字与节点昵称。</param>
    /// <param name="dynamics">按发布时间先后排列的动态与其卡片 PNG（绘制失败时为 null，改发文字）。</param>
    /// <returns>是否发送成功。</returns>
    public async Task<bool> PushDynamicsAsync(long groupId, string upName, IReadOnlyList<(BiliDynamic Dynamic, byte[]? Card)> dynamics)
    {
        long senderId = _context.Config<BiliConfig>().Value.ForwardSenderId;
        List<ForwardedMessageNode> nodes =
        [
            Node(senderId, upName, new MessageBody(ReplyTexts.Format(ReplyTexts.DYNAMIC_HEADER, upName))),
            Node(senderId, upName, new MessageBody(ReplyTexts.Format(ReplyTexts.DYNAMIC_COUNT, dynamics.Count)))
        ];
        foreach ((BiliDynamic dynamic, byte[]? card) in dynamics)
            nodes.AddRange(DynamicNodes(senderId, upName, dynamic, card));

        MessageBody message = new();
        message.Add(SegmentBuilder.Forward(nodes));
        return await SendAsync(groupId, message, $"UP {upName} 的 {dynamics.Count} 条动态");
    }

    /// <summary>发送纯文本（直播提醒）。</summary>
    /// <returns>是否发送成功。</returns>
    public Task<bool> PushTextAsync(long groupId, string text) => SendAsync(groupId, new MessageBody(text), "直播提醒");

    /// <summary>一条动态的节点：卡片；图片（单张直接、多张嵌套转发）；视频的封面原图与 BV 号。</summary>
    public static IEnumerable<ForwardedMessageNode> DynamicNodes(long senderId, string upName, BiliDynamic dynamic, byte[]? card)
    {
        yield return Node(senderId, upName, card is null ? new MessageBody(FallbackText(dynamic)) : new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(card)));

        if (dynamic.Pictures.Count == 1)
            yield return Node(senderId, upName, new MessageBody().AddImage(dynamic.Pictures[0].Url));
        else if (dynamic.Pictures.Count > 1)
        {
            MessageBody nested = new();
            nested.Add(SegmentBuilder.Forward([.. dynamic.Pictures.Select(picture => Node(senderId, upName, new MessageBody().AddImage(picture.Url)))]));
            yield return Node(senderId, upName, nested);
        }

        if (dynamic.Kind == BiliDynamicKind.Video && dynamic.Video is { } video)
        {
            if (video.Cover.Length > 0) yield return Node(senderId, upName, new MessageBody().AddImage(video.Cover));
            if (video.Bvid.Length > 0) yield return Node(senderId, upName, new MessageBody(video.Bvid));
        }
    }

    // 卡片绘制失败时改发文字，保证推送进度照常推进而不是每轮重试。
    private static string FallbackText(BiliDynamic dynamic)
    {
        string body = dynamic.Title ?? string.Concat(dynamic.Text.Select(text => text.Text));
        return body.Length > 0 ? $"{dynamic.AuthorName}{dynamic.Action}：{body}" : $"{dynamic.AuthorName}{dynamic.Action}";
    }

    private static ForwardedMessageNode Node(long senderId, string name, MessageBody body) =>
        new()
        {
            UserId     = (UserId)senderId,
            SenderName = name,
            Segments   = body
        };

    private async Task<bool> SendAsync(long groupId, MessageBody message, string what)
    {
        if (_context.Bot.Api is not IBotApi api)
        {
            _logger.LogWarning("未连接 LLBot，无法向群 {GroupId} 发送{What}", groupId, what);
            return false;
        }

        try
        {
            SendMessageResult result = await api.SendGroupMessageAsync((GroupId)groupId, message);
            if (result.IsSuccess)
            {
                _logger.LogInformation("已向群 {GroupId} 发送{What}", groupId, what);
                return true;
            }

            _logger.LogError("向群 {GroupId} 发送{What}失败：{Code} {Error}", groupId, what, result.Code, result.ErrorMessage);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "向群 {GroupId} 发送{What}失败", groupId, what);
        }

        return false;
    }
}
