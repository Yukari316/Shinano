namespace Shinano.Framework.Rendering.Models;

/// <summary>长图的等级，决定标题区左侧竖条的颜色，对应面板语义色。</summary>
public enum TextImageLevel
{
    /// <summary>无等级，不画竖条。</summary>
    None,

    /// <summary>正常（success）：已恢复或已完成。</summary>
    Normal,

    /// <summary>警告（warning）：功能降级或需要关注。</summary>
    Warning,

    /// <summary>错误（error）：功能已停止或操作失败。</summary>
    Error
}
