using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Shinano.Framework.Plugins;
using Shinano.Host.Browser;
using Shinano.Host.Browser.Models;
using Shinano.Tests.Infrastructure;

namespace Shinano.Tests.Browser;

/// <summary>
/// 以真实下载的 Chrome for Testing 验证浏览器服务：页面看到的 UA、品牌与自动化标记已覆盖为正式版 Chrome 的值，
/// 且与插件 HTTP 请求使用的身份一致；启动浏览器的测试不访问外部页面，只在设置 <c>SHINANO_TEST_BROWSER_DIR</c> 时运行。
/// </summary>
public sealed class BrowserServiceTests
{
    private const string PLUGIN_ID = "browser-test";

    private const string PROBE_SCRIPT = """
        () => ({
            userAgent: navigator.userAgent,
            brands: navigator.userAgentData.brands.map(b => b.brand),
            webdriver: navigator.webdriver
        })
        """;

    [Fact]
    public async Task OpenAsync_PresentsBrandedChromeWithoutAutomationMarkers()
    {
        TestEnvironment.RequireBrowser();
        // SAFE: RequireBrowser 在未设置时已跳过测试。
        BrowserService service = new(new BrowserOptions(), TestEnvironment.BrowserDirectory!, TimeProvider.System, NullLogger<BrowserService>.Instance);

        await using IBrowserSession session = await service.OpenAsync(PLUGIN_ID, TestContext.Current.CancellationToken);
        JsonElement probe = await session.Page.EvaluateFunctionAsync<JsonElement>(PROBE_SCRIPT);

        string userAgent = probe.GetProperty("userAgent").GetString() ?? "";
        Assert.DoesNotContain("Headless", userAgent);
        Assert.Equal(session.Identity.UserAgent, userAgent);
        Assert.Contains("Google Chrome", probe.GetProperty("brands").EnumerateArray().Select(brand => brand.GetString()));
        Assert.False(probe.GetProperty("webdriver").GetBoolean());
        Assert.True(Directory.Exists(service.ProfileDirectory(PLUGIN_ID)));
    }

    [Fact]
    public async Task ClearProfileAsync_DeletesOnlyThePluginProfile()
    {
        string data = Directory.CreateTempSubdirectory("shinano-browser-").FullName;
        try
        {
            BrowserService service = new(new BrowserOptions(), data, TimeProvider.System, NullLogger<BrowserService>.Instance);
            Directory.CreateDirectory(Path.Combine(service.ProfileDirectory(PLUGIN_ID), "Default"));
            Directory.CreateDirectory(service.ProfileDirectory("other"));

            await service.ClearProfileAsync(PLUGIN_ID, TestContext.Current.CancellationToken);
            await service.ClearProfileAsync(PLUGIN_ID, TestContext.Current.CancellationToken);

            Assert.False(Directory.Exists(service.ProfileDirectory(PLUGIN_ID)));
            Assert.True(Directory.Exists(service.ProfileDirectory("other")));
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }
}
