using Shinano.Framework.Plugins.Models;
using Shinano.Host.Browser.Models;

namespace Shinano.Host.Browser;

/// <summary>
/// 把无头 Chrome for Testing 报告的 UA 与 Client Hints 改写为同版本正式版 Chrome 的值：UA 中的 <c>HeadlessChrome/</c> 改为
/// <c>Chrome/</c>，品牌列表在 <c>Chromium</c> 之前补上 <c>Google Chrome</c>（CfT 与无头模式都没有该品牌）。其余值（平台版本、架构等）
/// 保留浏览器的真实值，不写死。
/// </summary>
public static class BrowserUserAgent
{
    /// <summary>页面内读取 UA 与 Client Hints 的脚本，结果为可反序列化为 <see cref="ClientHints" /> 的对象。</summary>
    public const string READ_SCRIPT = """
        async () => {
            const data = navigator.userAgentData;
            const high = await data.getHighEntropyValues(["architecture", "bitness", "model", "platformVersion", "fullVersionList", "wow64"]);
            return {
                userAgent: navigator.userAgent,
                navigatorPlatform: navigator.platform,
                brands: data.brands,
                fullVersionList: high.fullVersionList,
                platform: data.platform,
                platformVersion: high.platformVersion,
                architecture: high.architecture,
                bitness: high.bitness,
                model: high.model,
                mobile: data.mobile,
                wow64: high.wow64
            };
        }
        """;

    /// <summary>浏览器与 HTTP 请求统一使用的语言。</summary>
    public const string ACCEPT_LANGUAGE = "zh-CN,zh;q=0.9";

    private const string HEADLESS_TOKEN = "HeadlessChrome/";
    private const string CHROME_TOKEN   = "Chrome/";
    private const string CHROMIUM       = "Chromium";
    private const string HEADLESS_BRAND = "HeadlessChrome";
    private const string GOOGLE_CHROME  = "Google Chrome";

    /// <summary>改写为正式版 Chrome 的值。</summary>
    public static ClientHints Disguise(ClientHints raw) =>
        new()
        {
            UserAgent         = raw.UserAgent.Replace(HEADLESS_TOKEN, CHROME_TOKEN, StringComparison.Ordinal),
            NavigatorPlatform = raw.NavigatorPlatform,
            Brands            = WithGoogleChrome(raw.Brands),
            FullVersionList   = WithGoogleChrome(raw.FullVersionList),
            Platform          = raw.Platform,
            PlatformVersion   = raw.PlatformVersion,
            Architecture      = raw.Architecture,
            Bitness           = raw.Bitness,
            Model             = raw.Model,
            Mobile            = raw.Mobile,
            Wow64             = raw.Wow64
        };

    /// <summary>CDP <c>Network.setUserAgentOverride</c> 的参数。</summary>
    public static object OverrideCommand(ClientHints hints) =>
        new
        {
            userAgent      = hints.UserAgent,
            acceptLanguage = ACCEPT_LANGUAGE,
            platform       = hints.NavigatorPlatform,
            userAgentMetadata = new
            {
                brands          = hints.Brands.Select(Brand),
                fullVersionList = hints.FullVersionList.Select(Brand),
                platform        = hints.Platform,
                platformVersion = hints.PlatformVersion,
                architecture    = hints.Architecture,
                model           = hints.Model,
                mobile          = hints.Mobile,
                bitness         = hints.Bitness,
                wow64           = hints.Wow64
            }
        };

    /// <summary>HTTP 请求应携带的 UA 与低熵 Client Hints 请求头。</summary>
    public static BrowserIdentity Identity(ClientHints hints) =>
        new()
        {
            UserAgent       = hints.UserAgent,
            SecChUa         = string.Join(", ", hints.Brands.Select(brand => $"\"{brand.Brand}\";v=\"{brand.Version}\"")),
            SecChUaMobile   = hints.Mobile ? "?1" : "?0",
            SecChUaPlatform = $"\"{hints.Platform}\"",
            AcceptLanguage  = ACCEPT_LANGUAGE
        };

    // 正式版 Chrome 的品牌顺序由 GREASE 决定，Google Chrome 紧挨在 Chromium 之前；旧版无头的 HeadlessChrome 品牌直接替换。
    private static IReadOnlyList<BrandVersion> WithGoogleChrome(IReadOnlyList<BrandVersion> brands)
    {
        if (brands.Any(brand => brand.Brand == GOOGLE_CHROME)) return brands;

        List<BrandVersion> result = [.. brands.Where(brand => brand.Brand != HEADLESS_BRAND)];
        int chromium = result.FindIndex(brand => brand.Brand == CHROMIUM);
        if (chromium < 0) return result;

        result.Insert(
            chromium,
            new BrandVersion
            {
                Brand   = GOOGLE_CHROME,
                Version = result[chromium].Version
            });
        return result;
    }

    private static object Brand(BrandVersion brand) =>
        new
        {
            brand   = brand.Brand,
            version = brand.Version
        };
}
