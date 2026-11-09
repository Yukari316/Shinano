namespace Shinano.Host.Browser.Models;

/// <summary>浏览器在页面中报告的 UA 与 Client Hints（<c>navigator.userAgentData.getHighEntropyValues</c> 的结果）。</summary>
public sealed class ClientHints
{
    /// <summary><c>navigator.userAgent</c>。</summary>
    public required string UserAgent { get; init; }

    /// <summary><c>navigator.platform</c>，如 <c>Win32</c>。</summary>
    public required string NavigatorPlatform { get; init; }

    /// <summary>品牌与主版本号，按浏览器报告的顺序。</summary>
    public required IReadOnlyList<BrandVersion> Brands { get; init; }

    /// <summary>品牌与完整版本号，按浏览器报告的顺序。</summary>
    public required IReadOnlyList<BrandVersion> FullVersionList { get; init; }

    /// <summary>平台，如 <c>Windows</c>。</summary>
    public required string Platform { get; init; }

    /// <summary>平台版本。</summary>
    public required string PlatformVersion { get; init; }

    /// <summary>CPU 架构，如 <c>x86</c>。</summary>
    public required string Architecture { get; init; }

    /// <summary>位数，如 <c>64</c>。</summary>
    public required string Bitness { get; init; }

    /// <summary>设备型号，桌面为空。</summary>
    public required string Model { get; init; }

    /// <summary>是否移动设备。</summary>
    public required bool Mobile { get; init; }

    /// <summary>是否 WoW64。</summary>
    public required bool Wow64 { get; init; }
}
