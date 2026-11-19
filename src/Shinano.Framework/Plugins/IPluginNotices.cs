using Shinano.Framework.Rendering;

namespace Shinano.Framework.Plugins;

/// <summary>插件经宿主通知全部 Super User。</summary>
public interface IPluginNotices
{
    /// <summary>
    /// 在后台以宿主通知卡片私聊全部 Super User，不等待结果；卡片无法绘制时改发文字，未连接 IM 或发送失败只记录日志，不重试。
    /// 去重（如同一故障只通知一次）由调用方负责。
    /// </summary>
    void NotifySuperUsers(SuperUserNotice notice);
}

/// <summary>
/// 发给 Super User 的通知：绘制为宿主通知卡片，标题块右上为发送时间，正文只有原因与影响两行，详细信息以日志为准。不放凭据与路径。
/// </summary>
public sealed class SuperUserNotice
{
    /// <summary>等级，只由状态标与标签颜色表示。</summary>
    public required CardLevel Level { get; init; }

    /// <summary>标题。</summary>
    public required string Title { get; init; }

    /// <summary>通知种类的英文代号，显示在页脚左侧，如 <c>BILI-LOGIN-EXPIRED</c>。</summary>
    public required string Code { get; init; }

    /// <summary>原因；恢复类通知没有原因时为 null。</summary>
    public string? Cause { get; init; }

    /// <summary>影响，一句话。</summary>
    public required string Impact { get; init; }
}
