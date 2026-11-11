using System.ComponentModel.DataAnnotations;

namespace Shinano.Plugin.NB2Bridge.Models;

/// <summary>NB2Bridge 插件配置。事件类型开关与等待时间即时生效，其余项在下次启动 NB2 时生效。</summary>
public sealed class Nb2Config
{
    /// <summary>是否把群聊消息交付给 NB2。</summary>
    [Display(Name = "交付群聊消息", Order = 1)]
    public bool DeliverGroupMessages { get; set; } = true;

    /// <summary>是否把好友私聊消息交付给 NB2（临时会话一律不交付）。</summary>
    [Display(Name = "交付私聊消息", Description = "只交付好友私聊，临时会话不交付", Order = 2)]
    public bool DeliverPrivateMessages { get; set; } = true;

    /// <summary>是否把通知事件（撤回、戳一戳、群成员变动等）交付给 NB2。</summary>
    [Display(Name = "交付通知事件", Description = "撤回、戳一戳、群成员与群资料变动等", Order = 3)]
    public bool DeliverNotices { get; set; }

    /// <summary>是否把请求事件（好友申请、入群申请与邀请）交付给 NB2。</summary>
    [Display(Name = "交付请求事件", Description = "好友申请、入群申请与入群邀请", Order = 4)]
    public bool DeliverRequests { get; set; }

    /// <summary>等待 NB2 判定是否处理该事件的上限；超时视为继续。</summary>
    [Display(Name = "等待处理判定（秒）", Description = "NB2 在这段时间内没有判定是否开始处理时，后续插件照常处理该消息", Order = 5)]
    [Range(1, 60)]
    public int EventTimeoutSeconds { get; set; } = 5;

    /// <summary>1 分钟内允许的自动重启次数，超过后停止并通知 Super User。</summary>
    [Display(Name = "每分钟最多自动重启次数", Description = "NB2 进程意外退出时自动重启；1 分钟内超过这个次数后停止并通知 Super User", Order = 6)]
    [Range(0, 10)]
    public int RestartLimit { get; set; } = 3;

    /// <summary>停止 NB2 时等待正常退出的秒数，超时强制结束。</summary>
    [Display(Name = "正常停止等待（秒）", Order = 7)]
    [Range(1, 120)]
    public int StopTimeoutSeconds { get; set; } = 10;

    /// <summary>单个 NB2 日志文件的大小上限，超过后轮转。</summary>
    [Display(Name = "日志文件大小上限（MiB）", Order = 8)]
    [Range(1, 1024)]
    public int LogFileSizeMiB { get; set; } = 10;

    /// <summary>保留的 NB2 日志文件个数（含当前文件）。</summary>
    [Display(Name = "日志文件保留个数", Order = 9)]
    [Range(1, 50)]
    public int LogFileCount { get; set; } = 5;
}
