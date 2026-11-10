using System.ComponentModel.DataAnnotations;

namespace Shinano.Plugin.Bilibili.Models;

/// <summary>B站订阅插件配置。登录状态由宿主浏览器的用户目录保存，不在配置中。</summary>
public sealed class BiliConfig
{
    /// <summary>动态与直播的轮询间隔，单位分钟。</summary>
    [Display(Name = "轮询间隔（分钟）", Description = "每隔多久检查一次关注的 UP 的新动态与直播状态，修改后下一轮生效", Order = 1)]
    [Range(1, 1440)]
    public int PollIntervalMinutes { get; set; } = 10;

    /// <summary>私聊登录时已有登录、等待确认是否退出的时间，单位秒。</summary>
    [Display(Name = "退出登录确认等待（秒）", Description = "已有登录时私聊「B站登录」，等待 Super User 回复 y 或 n 的时间", Order = 2)]
    [Range(10, 600)]
    public int ConfirmTimeoutSeconds { get; set; } = 60;

    /// <summary>合并转发节点显示的发送者 QQ 号，节点昵称为 UP 名；不使用 Bot 自己的身份。</summary>
    [Display(Name = "转发节点 QQ 号", Description = "动态推送的合并转发中每条消息显示的发送者 QQ 号（显示其头像），昵称为 UP 名", Order = 3)]
    [Range(10000, long.MaxValue)]
    public long ForwardSenderId { get; set; } = 114514;

    /// <summary>图文与纯文字动态的卡片正文最多显示的行数；超出时截断并附加原文链接。</summary>
    [Display(Name = "图文与纯文字最多行数", Description = "卡片正文超过这个行数时截断，并在附加内容中提示原文太长与原文链接", Order = 4)]
    [Range(1, 200)]
    public int OpusMaxLines { get; set; } = 20;

    /// <summary>专栏摘要最多显示的行数；专栏在卡片中只有摘要，总是附加原文链接。</summary>
    [Display(Name = "专栏摘要最多行数", Description = "专栏在卡片中只显示摘要，总会附加原文链接", Order = 5)]
    [Range(1, 200)]
    public int ArticleMaxLines { get; set; } = 20;

    /// <summary>转发动态的转发内容与原动态各自最多显示的行数；超出时截断并附加原文链接。</summary>
    [Display(Name = "转发动态最多行数", Description = "转发内容与原动态的正文各自计算，超过时截断并附加原文链接", Order = 6)]
    [Range(1, 200)]
    public int ForwardMaxLines { get; set; } = 10;

    /// <summary>视频简介最多显示的行数；视频另发 BV 号，超出时不附加原文链接。</summary>
    [Display(Name = "视频简介最多行数", Description = "视频另发 BV 号，简介超出时只截断，不附加原文链接", Order = 7)]
    [Range(1, 200)]
    public int VideoDescLines { get; set; } = 3;

    /// <summary>其他类型动态的卡片正文最多显示的行数；超出时截断并附加原文链接。</summary>
    [Display(Name = "其他动态最多行数", Description = "以上类型以外的动态，正文超过这个行数时截断并附加原文链接", Order = 8)]
    [Range(1, 200)]
    public int GenericMaxLines { get; set; } = 20;
}
