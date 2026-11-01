using Shinano.Framework.Rendering;
using SkiaSharp;

namespace Shinano.Framework.Plugins;

/// <summary>
/// 宿主提供的公共字体：Noto Sans SC 常规与粗体、Twemoji Mozilla 彩色 emoji。SkiaSharp 由宿主与插件共享，
/// 返回的字体可直接用于插件自绘；字体由宿主持有，插件不得释放。公共字体不够用的插件自行在 <c>assets/</c> 中提供。
/// </summary>
public interface IPluginFonts
{
    /// <summary>中文常规体。</summary>
    /// <exception cref="InvalidOperationException">宿主字体文件缺失，无法加载。</exception>
    SKTypeface Regular { get; }

    /// <summary>中文粗体。</summary>
    /// <exception cref="InvalidOperationException">宿主字体文件缺失，无法加载。</exception>
    SKTypeface Bold { get; }

    /// <summary>彩色 emoji。</summary>
    /// <exception cref="InvalidOperationException">宿主字体文件缺失，无法加载。</exception>
    SKTypeface Emoji { get; }

    /// <summary>以 <see cref="Emoji" /> 为回退字体的文字绘制辅助。</summary>
    /// <exception cref="InvalidOperationException">宿主字体文件缺失，无法加载。</exception>
    TextPainter Painter { get; }
}
