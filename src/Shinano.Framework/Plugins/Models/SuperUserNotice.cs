using Shinano.Framework.Rendering.Models;

namespace Shinano.Framework.Plugins.Models;

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
