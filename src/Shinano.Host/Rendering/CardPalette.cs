using SkiaSharp;

namespace Shinano.Host.Rendering;

/// <summary>
/// 消息图片的具名色板，取自 icaruszezen/endfield-ui 的《终末地》色彩令牌（亮色主题）。与管理面板的 daisyUI 主题无关；
/// 绘制代码只引用这里的名字，不直接写色值。
/// </summary>
internal static class CardPalette
{
    // 点阵约 8% 不透明度（255 × 0.084），去掉后图片仍成立，只作底纹。
    private const byte DOT_ALPHA = 21;

    /// <summary>纸白：面板底色。</summary>
    public static readonly SKColor Paper = SKColor.Parse("#FFFFFF");

    /// <summary>凹陷：外框底色、行内代码与引用的底色。</summary>
    public static readonly SKColor Sunken = SKColor.Parse("#F2F2F2");

    /// <summary>淡灰：表头底色。</summary>
    public static readonly SKColor Muted = SKColor.Parse("#E6E6E6");

    /// <summary>墨：主文字、反转标题块与代码块底色。</summary>
    public static readonly SKColor Ink = SKColor.Parse("#191919");

    /// <summary>深墨：标题文字。</summary>
    public static readonly SKColor InkStrong = SKColor.Parse("#141414");

    /// <summary>次墨：标签与次级文字。</summary>
    public static readonly SKColor InkSecondary = SKColor.Parse("#626262");

    /// <summary>三级墨：序号、页脚等装饰文字。</summary>
    public static readonly SKColor InkTertiary = SKColor.Parse("#8A8A8A");

    /// <summary>分隔线。</summary>
    public static readonly SKColor Line = SKColor.Parse("#D9D9D9");

    /// <summary>强分隔线：刻度、反转块上的次级文字。</summary>
    public static readonly SKColor LineStrong = SKColor.Parse("#B3B3B3");

    /// <summary>信号黄：强调填充（斜楔、第一名、强调底）。白底上对比度不足，不能作文字。</summary>
    public static readonly SKColor Signal = SKColor.Parse("#FFFA00");

    /// <summary>信号墨：亮色表面上的强调文字。</summary>
    public static readonly SKColor SignalInk = SKColor.Parse("#6B5D00");

    /// <summary>错误填充色。</summary>
    public static readonly SKColor Error = SKColor.Parse("#F62A2A");

    /// <summary>警告填充色。</summary>
    public static readonly SKColor Warning = SKColor.Parse("#FC6C00");

    /// <summary>正常填充色（状态项）。</summary>
    public static readonly SKColor Normal = SKColor.Parse("#00D872");

    /// <summary>点阵：外框底纹的墨色点（约 8% 不透明度）。</summary>
    public static readonly SKColor Dot = Ink.WithAlpha(DOT_ALPHA);

    /// <summary>斜纹：分隔线底纹。</summary>
    public static readonly SKColor Hatch = LineStrong;
}
