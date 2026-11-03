using Shinano.Host.Rendering;
using Shinano.Host.Rendering.Models;

namespace Shinano.Tests.Rendering;

/// <summary>测试共用的宿主字体与消息图片绘制器，字体随宿主资源输出到测试目录。</summary>
internal static class TestRendering
{
    /// <summary>输出宽度（像素），取部署配置的默认值。</summary>
    public const int WIDTH = 1200;

    /// <summary>宿主字体。</summary>
    public static HostFonts Fonts { get; } = new(AppContext.BaseDirectory);

    /// <summary>消息图片绘制器。</summary>
    public static CardRenderer Images { get; } = new(Fonts, new RenderingOptions { Width = WIDTH });
}
