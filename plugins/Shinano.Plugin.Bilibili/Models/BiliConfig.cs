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
}
