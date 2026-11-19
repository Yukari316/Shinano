namespace Shinano.Host.Panel.Appearance;

/// <summary>
/// 部署级的面板外观设置。没有背景图时页面与面板都是纯色，以下不透明度与模糊只在设置背景图后生效；背景图本身始终不透明。
/// </summary>
public sealed class AppearanceSettings
{
    /// <summary>默认的面板底色不透明度。</summary>
    public const int DEFAULT_PANEL_OPACITY = 80;

    /// <summary>默认的内容区卡片不透明度。</summary>
    public const int DEFAULT_CARD_OPACITY = 80;

    /// <summary>默认的内容区遮罩不透明度。</summary>
    public const int DEFAULT_VEIL_OPACITY = 40;

    /// <summary>侧边栏与日志面板底色的不透明度，0–100。</summary>
    public int PanelOpacity { get; set; } = DEFAULT_PANEL_OPACITY;

    /// <summary>内容区卡片（含页面工具栏与底部操作栏）底色的不透明度，0–100。</summary>
    public int CardOpacity { get; set; } = DEFAULT_CARD_OPACITY;

    /// <summary>页面内容区遮罩的不透明度，0–100。</summary>
    public int VeilOpacity { get; set; } = DEFAULT_VEIL_OPACITY;

    /// <summary>是否对整张背景图做磨砂模糊，默认关闭以保持背景图清晰。</summary>
    public bool Blur { get; set; }

    /// <summary>背景图文件名，位于外观目录；没有背景图时为 null。</summary>
    public string? BackgroundFile { get; set; }
}

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
