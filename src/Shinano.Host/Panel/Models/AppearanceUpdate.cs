namespace Shinano.Host.Panel.Models;

/// <summary>修改外观设置的请求；不透明度超出 0–100 时截断。</summary>
public sealed class AppearanceUpdate
{
    /// <summary>面板底色不透明度。</summary>
    public int PanelOpacity { get; set; } = AppearanceSettings.DEFAULT_PANEL_OPACITY;

    /// <summary>内容区卡片不透明度。</summary>
    public int CardOpacity { get; set; } = AppearanceSettings.DEFAULT_CARD_OPACITY;

    /// <summary>内容区遮罩不透明度。</summary>
    public int VeilOpacity { get; set; } = AppearanceSettings.DEFAULT_VEIL_OPACITY;

    /// <summary>是否启用磨砂模糊。</summary>
    public bool Blur { get; set; }
}
