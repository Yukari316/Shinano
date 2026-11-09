using Shinano.Framework.Plugins.Models;
using Shinano.Host.Browser;
using Shinano.Host.Browser.Models;

namespace Shinano.Tests.Browser;

public sealed class BrowserUserAgentTests
{
    private const string HEADLESS_UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/157.0.0.0 Safari/537.36";

    [Fact]
    public void Disguise_ReplacesHeadlessTokenAndAddsGoogleChromeBeforeChromium()
    {
        ClientHints hints = BrowserUserAgent.Disguise(
            Hints(
                HEADLESS_UA,
                [Brand("Chromium", "157"), Brand("Not-A.Brand", "99")],
                [Brand("Chromium", "157.0.8103.38"), Brand("Not-A.Brand", "99.0.0.0")]));

        Assert.Equal(HEADLESS_UA.Replace("HeadlessChrome/", "Chrome/"), hints.UserAgent);
        Assert.Equal(["Google Chrome", "Chromium", "Not-A.Brand"], hints.Brands.Select(brand => brand.Brand));
        Assert.Equal("157", hints.Brands[0].Version);
        Assert.Equal("157.0.8103.38", hints.FullVersionList[0].Version);
        Assert.Equal("15.0.0", hints.PlatformVersion);
    }

    [Fact]
    public void Disguise_ReplacesHeadlessBrandAndKeepsExistingGoogleChrome()
    {
        ClientHints legacy = BrowserUserAgent.Disguise(
            Hints(HEADLESS_UA, [Brand("Not-A.Brand", "99"), Brand("HeadlessChrome", "157"), Brand("Chromium", "157")], []));
        ClientHints branded = BrowserUserAgent.Disguise(
            Hints(HEADLESS_UA, [Brand("Not-A.Brand", "99"), Brand("Google Chrome", "157"), Brand("Chromium", "157")], []));

        Assert.Equal(["Not-A.Brand", "Google Chrome", "Chromium"], legacy.Brands.Select(brand => brand.Brand));
        Assert.Equal(["Not-A.Brand", "Google Chrome", "Chromium"], branded.Brands.Select(brand => brand.Brand));
    }

    [Fact]
    public void Identity_FormatsLowEntropyHeaders()
    {
        BrowserIdentity identity = BrowserUserAgent.Identity(
            BrowserUserAgent.Disguise(Hints(HEADLESS_UA, [Brand("Not-A.Brand", "99"), Brand("Chromium", "157")], [])));

        Assert.Equal("\"Not-A.Brand\";v=\"99\", \"Google Chrome\";v=\"157\", \"Chromium\";v=\"157\"", identity.SecChUa);
        Assert.Equal("?0", identity.SecChUaMobile);
        Assert.Equal("\"Windows\"", identity.SecChUaPlatform);
        Assert.DoesNotContain("Headless", identity.UserAgent);
    }

    private static ClientHints Hints(string userAgent, BrandVersion[] brands, BrandVersion[] fullVersions) =>
        new()
        {
            UserAgent         = userAgent,
            NavigatorPlatform = "Win32",
            Brands            = brands,
            FullVersionList   = fullVersions,
            Platform          = "Windows",
            PlatformVersion   = "15.0.0",
            Architecture      = "x86",
            Bitness           = "64",
            Model             = "",
            Mobile            = false,
            Wow64             = false
        };

    private static BrandVersion Brand(string name, string version) =>
        new()
        {
            Brand   = name,
            Version = version
        };
}
