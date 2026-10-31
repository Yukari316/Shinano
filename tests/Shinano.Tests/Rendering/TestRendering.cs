using Shinano.Host.Rendering;

namespace Shinano.Tests.Rendering;

/// <summary>测试共用的宿主字体与长图绘制器，字体随宿主资源输出到测试目录。</summary>
internal static class TestRendering
{
    /// <summary>宿主字体。</summary>
    public static HostFonts Fonts { get; } = new(AppContext.BaseDirectory);

    /// <summary>长图绘制器。</summary>
    public static TextImageRenderer Images { get; } = new(Fonts);
}
