using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using Shinano.Framework.Plugins;

namespace Shinano.Host.Tools;

/// <summary>一个浏览器进程的会话：每个页面在使用前应用 UA 与 Client Hints 覆盖；释放时关闭进程并让出插件的会话锁。</summary>
internal sealed class BrowserSession : IBrowserSession
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // .invalid 顶级域保证不会解析到真实主机。
    private const string PROBE_URL = "https://probe.shinano.invalid/";

    private readonly IBrowser    _browser;
    private readonly ClientHints _hints;
    private readonly Action      _release;
    private readonly ILogger     _logger;
    private          bool        _disposed;

    private BrowserSession(IBrowser browser, IPage page, ClientHints hints, Action release, ILogger logger)
    {
        _browser = browser;
        Page     = page;
        _hints   = hints;
        Identity = BrowserUserAgent.Identity(hints);
        _release = release;
        _logger  = logger;
    }

    /// <inheritdoc />
    public IPage Page { get; }

    /// <inheritdoc />
    public BrowserIdentity Identity { get; }

    /// <summary>读取浏览器报告的 Client Hints，改写后应用到首个页面。失败时关闭浏览器并抛出。</summary>
    /// <param name="browser">已启动的浏览器；本方法取得其所有权。</param>
    /// <param name="release">会话结束时调用一次，让出插件的会话锁。</param>
    /// <param name="logger">日志。</param>
    public static async Task<BrowserSession> StartAsync(IBrowser browser, Action release, ILogger logger)
    {
        try
        {
            IPage page = (await browser.PagesAsync()).FirstOrDefault() ?? await browser.NewPageAsync();
            ClientHints hints = BrowserUserAgent.Disguise(await ReadHintsAsync(page));
            await ApplyAsync(page, hints);
            return new BrowserSession(browser, page, hints, release, logger);
        }
        catch
        {
            await browser.CloseAsync();
            throw;
        }
    }

    // navigator.userAgentData 只在安全上下文中存在，about:blank 没有；打开一个由本地拦截应答的 https 地址读取，不产生网络请求。
    private static async Task<ClientHints> ReadHintsAsync(IPage page)
    {
        await page.SetRequestInterceptionAsync(true);
        page.Request += RespondBlank;
        try
        {
            await page.GoToAsync(PROBE_URL);
            JsonElement result = await page.EvaluateFunctionAsync<JsonElement>(BrowserUserAgent.READ_SCRIPT);
            return result.Deserialize<ClientHints>(JsonOptions) ?? throw new InvalidOperationException("浏览器没有返回 Client Hints");
        }
        finally
        {
            page.Request -= RespondBlank;
            await page.SetRequestInterceptionAsync(false);
        }
    }

    private static void RespondBlank(object? sender, RequestEventArgs e) =>
        _ = e.Request.RespondAsync(
            new ResponseData
            {
                Status      = HttpStatusCode.OK,
                ContentType = "text/html",
                Body        = "<!doctype html><title></title>"
            });

    /// <inheritdoc />
    public async Task<IPage> NewPageAsync()
    {
        IPage page = await _browser.NewPageAsync();
        await ApplyAsync(page, _hints);
        return page;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await _browser.CloseAsync().WaitAsync(CloseTimeout);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "关闭浏览器失败，结束其进程");
            _browser.Process?.Kill(entireProcessTree: true);
        }
        finally
        {
            await _browser.DisposeAsync();
            _release();
        }
    }

    // PuppeteerSharp 的 SetUserAgentAsync 不能设置 fullVersionList，且与原始命令混用时 Worker 取到旧值，只用原始命令。
    private static Task ApplyAsync(IPage page, ClientHints hints) =>
        page.Client.SendAsync("Network.setUserAgentOverride", BrowserUserAgent.OverrideCommand(hints));
}
