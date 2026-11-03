using Shinano.Framework.Rendering;
using SkiaSharp;

namespace Shinano.Framework.Plugins;

/// <summary>
/// 宿主提供的公共字体：Noto Sans SC 常规与粗体（中文与通用正文）、Space Grotesk 中粗与粗体（英文标签与数字）、
/// JetBrains Mono（代码）、Twemoji Mozilla 彩色 emoji。SkiaSharp 由宿主与插件共享，返回的字体可直接用于插件自绘；
/// 字体由宿主持有，插件不得释放。公共字体不够用的插件自行在 <c>assets/</c> 中提供。
/// </summary>
public interface IPluginFonts
{
    /// <summary>中文常规体。</summary>
    /// <exception cref="InvalidOperationException">宿主字体文件缺失，无法加载。</exception>
    SKTypeface Regular { get; }

    /// <summary>中文粗体。</summary>
    /// <exception cref="InvalidOperationException">宿主字体文件缺失，无法加载。</exception>
    SKTypeface Bold { get; }

    /// <summary>英文中粗体，用于英文标签与数字；缺少的汉字由 <see cref="Painter" /> 回退到中文字体。</summary>
    /// <exception cref="InvalidOperationException">宿主字体文件缺失，无法加载。</exception>
    SKTypeface Latin { get; }

    /// <summary>英文粗体，用于醒目的数字。</summary>
    /// <exception cref="InvalidOperationException">宿主字体文件缺失，无法加载。</exception>
    SKTypeface LatinBold { get; }

    /// <summary>等宽体，用于代码。</summary>
    /// <exception cref="InvalidOperationException">宿主字体文件缺失，无法加载。</exception>
    SKTypeface Mono { get; }

    /// <summary>彩色 emoji。</summary>
    /// <exception cref="InvalidOperationException">宿主字体文件缺失，无法加载。</exception>
    SKTypeface Emoji { get; }

    /// <summary>文字绘制辅助：主字体缺字形时依次回退到 <see cref="Regular" /> 与 <see cref="Emoji" />。</summary>
    /// <exception cref="InvalidOperationException">宿主字体文件缺失，无法加载。</exception>
    TextPainter Painter { get; }
}
