using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Sora.Core.Types;
using Sora.Entities.Interfaces;
using Sora.Entities.Message;

namespace Shinano.Plugin.Bilibili.Feed;

/// <summary>
/// 向群发送动态与直播提醒。一条动态由卡片与「附加内容」合并转发组成，附加内容依次为截断提示与原文链接、平铺的图片、
/// 视频封面原图与 BV 号，没有时省略。一个 UP 只有一条动态时先直接发卡片，再发附加内容；多条时为一条合并转发：
/// 「&lt;UP名&gt;更新了喵」「一共&lt;n&gt;条动态喵」，之后每条动态的卡片与嵌套的附加内容。节点发送者为插件配置的 QQ 号与 UP 名，
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

    /// <summary>把一个 UP 的若干条动态发到群：一条时为卡片与附加内容两条消息，多条时为一条合并转发。</summary>
    /// <param name="groupId">群号。</param>
    /// <param name="upName">UP 名，用作开头文字与节点昵称。</param>
    /// <param name="dynamics">按发布时间先后排列的动态与其卡片（绘制失败时为 null，改发文字）。</param>
    /// <returns>卡片（多条时为合并转发）是否发送成功；单条时附加内容发送失败只记日志，不影响结果，避免下一轮重发卡片。</returns>
    public async Task<bool> PushDynamicsAsync(long groupId, string upName, IReadOnlyList<(BiliDynamic Dynamic, BiliCard? Card)> dynamics)
    {
        long senderId = _context.Config<BiliConfig>().Value.ForwardSenderId;
        if (dynamics.Count > 1) return await SendAsync(groupId, Forward(DynamicsNodes(senderId, upName, dynamics)), $"UP {upName} 的 {dynamics.Count} 条动态");

        (BiliDynamic dynamic, BiliCard? card) = dynamics[0];
        if (!await SendAsync(groupId, CardBody(dynamic, card), $"UP {upName} 的动态 {dynamic.Id}")) return false;
        List<ForwardedMessageNode> extras = [.. ExtraNodes(senderId, upName, dynamic, card)];
        if (extras.Count > 0) await SendAsync(groupId, Forward(extras), $"UP {upName} 的动态 {dynamic.Id} 的附加内容");
        return true;
    }

    /// <summary>发送纯文本（直播提醒）。</summary>
    /// <returns>是否发送成功。</returns>
    public Task<bool> PushTextAsync(long groupId, string text) => SendAsync(groupId, new MessageBody(text), "直播提醒");

    /// <summary>多条动态合并转发的节点：开头两条文字，之后每条动态的卡片与嵌套的附加内容（没有时省略）。</summary>
    public static IEnumerable<ForwardedMessageNode> DynamicsNodes(long senderId, string upName, IReadOnlyList<(BiliDynamic Dynamic, BiliCard? Card)> dynamics)
    {
        yield return Node(senderId, upName, new MessageBody(ReplyTexts.Format(ReplyTexts.DYNAMIC_HEADER, upName)));
        yield return Node(senderId, upName, new MessageBody(ReplyTexts.Format(ReplyTexts.DYNAMIC_COUNT, dynamics.Count)));
        foreach ((BiliDynamic dynamic, BiliCard? card) in dynamics)
        {
            yield return Node(senderId, upName, CardBody(dynamic, card));
            List<ForwardedMessageNode> extras = [.. ExtraNodes(senderId, upName, dynamic, card)];
            if (extras.Count > 0) yield return Node(senderId, upName, Forward(extras));
        }
    }

    /// <summary>一条动态的附加内容节点：正文截断时的提示与原文链接；图片每张一个节点；视频的封面原图与 BV 号。</summary>
    public static IEnumerable<ForwardedMessageNode> ExtraNodes(long senderId, string upName, BiliDynamic dynamic, BiliCard? card)
    {
        if (card?.MoreLinkId is { } linkId) yield return Node(senderId, upName, new MessageBody(ReplyTexts.Format(ReplyTexts.TEXT_TOO_LONG, linkId)));
        foreach (BiliPicture picture in dynamic.Pictures) yield return Node(senderId, upName, new MessageBody().AddImage(picture.Url));

        if (dynamic.Kind == BiliDynamicKind.Video && dynamic.Video is { } video)
        {
            if (video.Cover.Length > 0) yield return Node(senderId, upName, new MessageBody().AddImage(video.Cover));
            if (video.Bvid.Length > 0) yield return Node(senderId, upName, new MessageBody(video.Bvid));
        }
    }

    /// <summary>卡片图片；绘制失败时为文字。</summary>
    public static MessageBody CardBody(BiliDynamic dynamic, BiliCard? card) =>
        card is null ? new MessageBody(FallbackText(dynamic)) : new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(card.Png));

    private static MessageBody Forward(IEnumerable<ForwardedMessageNode> nodes)
    {
        MessageBody message = new();
        message.Add(SegmentBuilder.Forward([.. nodes]));
        return message;
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
