using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using PuppeteerSharp.BrowserData;
using Shinano.Framework.Plugins;

namespace Shinano.Host.Tools;

/// <summary>
/// 宿主浏览器服务：解析并下载 Chrome for Testing 到部署数据目录，按插件启动使用其持久用户目录的无头浏览器。
/// 同一插件的会话串行；不同插件可以同时各有一个浏览器进程。线程安全。
/// </summary>
public sealed class BrowserService
{
    /// <summary>浏览器数据所在目录名，位于部署数据目录下。</summary>
    public const string DIRECTORY_NAME = "browser";

    private const string CHROME_DIRECTORY   = "chrome";
    private const string PROFILES_DIRECTORY = "profiles";
    private const string STABLE_URL         = "https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions.json";
    private const string MILESTONES_URL     = "https://googlechromelabs.github.io/chrome-for-testing/latest-versions-per-milestone.json";
    private const string STABLE_CHANNEL     = "Stable";
    private const int    LAUNCH_TIMEOUT_MS  = 60_000;

    private static readonly TimeSpan ResolveInterval = TimeSpan.FromDays(1);

    private static readonly string[] BaseArguments =
    [
        "--disable-blink-features=AutomationControlled",
        "--lang=zh-CN",
        "--window-size=1920,1080"
    ];

    private readonly BrowserOptions                            _options;
    private readonly string                                    _dataDirectory;
    private readonly string                                    _root;
    private readonly HttpClient                                _http;
    private readonly TimeProvider                              _time;
    private readonly ILogger<BrowserService>                   _logger;
    private readonly SemaphoreSlim                             _install = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessions = new();
    private          string?                                   _executable;
    private          DateTimeOffset                            _resolvedAt;

    /// <summary>创建服务；不访问网络，第一次打开会话时才解析与下载浏览器。</summary>
    /// <param name="options">浏览器配置。</param>
    /// <param name="dataDirectory">部署数据目录。</param>
    /// <param name="time">时间来源。</param>
    /// <param name="logger">日志。</param>
    public BrowserService(BrowserOptions options, string dataDirectory, TimeProvider time, ILogger<BrowserService> logger)
    {
        _options = options;
        _dataDirectory = dataDirectory;
        _root    = Path.Combine(dataDirectory, DIRECTORY_NAME);
        _http    = new HttpClient();
        _time    = time;
        _logger  = logger;
    }

    /// <summary>插件的持久用户目录。</summary>
    public string ProfileDirectory(string pluginId) => Path.Combine(_root, PROFILES_DIRECTORY, pluginId);

    /// <summary>为插件启动浏览器并打开会话；同一插件的前一个会话释放之前等待。</summary>
    /// <exception cref="InvalidOperationException">浏览器无法解析、下载或启动。</exception>
    public async Task<IBrowserSession> OpenAsync(string pluginId, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _sessions.GetOrAdd(pluginId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            string executable = await ExecutableAsync(cancellationToken);
            string profile = ProfileDirectory(pluginId);
            Directory.CreateDirectory(profile);
            IBrowser browser;
            try
            {
                browser = await Puppeteer.LaunchAsync(LaunchOptions(executable, profile));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "插件 {PluginId} 启动浏览器失败：{Executable}", pluginId, executable);
                throw new InvalidOperationException($"浏览器启动失败：{exception.Message}", exception);
            }

            BrowserSession session = await BrowserSession.StartAsync(browser, () => gate.Release(), _logger);
            _logger.LogInformation("插件 {PluginId} 已启动浏览器 {Version}", pluginId, session.Identity.UserAgent);
            return session;
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    /// <summary>等待插件当前的会话结束后删除其用户目录；目录不存在时不做任何事。</summary>
    public async Task ClearProfileAsync(string pluginId, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _sessions.GetOrAdd(pluginId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            string profile = ProfileDirectory(pluginId);
            if (!Directory.Exists(profile)) return;
            Directory.Delete(profile, recursive: true);
            _logger.LogInformation("已删除插件 {PluginId} 的浏览器用户目录", pluginId);
        }
        finally
        {
            gate.Release();
        }
    }

    // 必须给 IgnoredDefaultArgs 非空数组：赋空数组会丢掉全部默认参数（含无头与用户目录），只有非空数组才是逐项剔除。
    private static LaunchOptions LaunchOptions(string executable, string profile) =>
        new()
        {
            ExecutablePath     = executable,
            HeadlessMode       = HeadlessMode.True,
            UserDataDir        = profile,
            IgnoredDefaultArgs = ["--enable-automation"],
            Args               = OperatingSystem.IsLinux() ? [.. BaseArguments, "--no-sandbox"] : BaseArguments,
            DefaultViewport    = null,
            Timeout            = LAUNCH_TIMEOUT_MS
        };

    private async Task<string> ExecutableAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            string configured = Path.GetFullPath(_options.ExecutablePath, _dataDirectory);
            return File.Exists(configured)
                ? configured
                : throw new InvalidOperationException($"配置的浏览器路径不存在：{configured}");
        }

        await _install.WaitAsync(cancellationToken);
        try
        {
            bool fresh = _options.VersionMode != BrowserVersionMode.Latest || _time.GetUtcNow() - _resolvedAt < ResolveInterval;
            if (_executable is not null && fresh) return _executable;

            BrowserFetcher fetcher = Fetcher(SupportedBrowser.Chrome);
            string buildId = await ResolveBuildIdAsync(fetcher, cancellationToken);
            InstalledBrowser installed = fetcher.GetInstalledBrowsers().FirstOrDefault(item => item.BuildId == buildId)
                                         ?? await DownloadAsync(fetcher, buildId);
            RemoveOtherVersions(buildId);
            _executable = installed.GetExecutablePath();
            _resolvedAt = _time.GetUtcNow();
            return _executable;
        }
        finally
        {
            _install.Release();
        }
    }

    // 解析失败（如网络不通）时，已下载过的版本中有符合要求的就继续使用，避免外部服务短暂不可用导致浏览器不可用。
    private async Task<string> ResolveBuildIdAsync(BrowserFetcher fetcher, CancellationToken cancellationToken)
    {
        if (_options.VersionMode == BrowserVersionMode.Pinned) return Chrome.DefaultBuildId;

        try
        {
            return _options.VersionMode == BrowserVersionMode.Specific
                ? await MilestoneBuildAsync(_options.Version, cancellationToken)
                : await StableBuildAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException
                                          && !cancellationToken.IsCancellationRequested)
        {
            string? fallback = fetcher.GetInstalledBrowsers()
                                      .Select(item => item.BuildId)
                                      .Where(MatchesMode)
                                      .OrderByDescending(Version.Parse)
                                      .FirstOrDefault();
            if (fallback is null)
            {
                _logger.LogError(exception, "解析浏览器版本失败（{Mode}），本机也没有可用的已下载版本", _options.VersionMode);
                throw new InvalidOperationException($"无法解析浏览器版本：{exception.Message}", exception);
            }

            _logger.LogWarning(exception, "解析浏览器版本失败（{Mode}），继续使用已下载的 {BuildId}", _options.VersionMode, fallback);
            return fallback;
        }
    }

    private bool MatchesMode(string buildId) =>
        _options.VersionMode != BrowserVersionMode.Specific
        || buildId.StartsWith(_options.Version.ToString(CultureInfo.InvariantCulture) + ".", StringComparison.Ordinal);

    private async Task<string> StableBuildAsync(CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(await _http.GetStringAsync(STABLE_URL, cancellationToken));
        return document.RootElement.GetProperty("channels").GetProperty(STABLE_CHANNEL).GetProperty("version").GetString()
               ?? throw new JsonException("稳定版版本号为空");
    }

    private async Task<string> MilestoneBuildAsync(int milestone, CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(await _http.GetStringAsync(MILESTONES_URL, cancellationToken));
        string key = milestone.ToString(CultureInfo.InvariantCulture);
        return document.RootElement.GetProperty("milestones").TryGetProperty(key, out JsonElement entry)
            ? entry.GetProperty("version").GetString() ?? throw new JsonException($"主版本 {key} 的版本号为空")
            : throw new KeyNotFoundException($"Chrome for Testing 没有主版本 {key}");
    }

    private async Task<InstalledBrowser> DownloadAsync(BrowserFetcher fetcher, string buildId)
    {
        _logger.LogInformation("开始下载 Chrome for Testing {BuildId} 到 {Directory}", buildId, fetcher.CacheDir);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            InstalledBrowser installed = await fetcher.DownloadAsync(buildId);
            _logger.LogInformation("已下载 Chrome for Testing {BuildId}，用时 {Seconds:0.0} 秒", buildId, stopwatch.Elapsed.TotalSeconds);
            return installed;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "下载 Chrome for Testing {BuildId} 失败", buildId);
            throw new InvalidOperationException($"浏览器下载失败：{exception.Message}", exception);
        }
    }

    // 下载 Chrome 时 PuppeteerSharp 会连带下载 chrome-headless-shell，两者的旧版本都删除。
    private void RemoveOtherVersions(string buildId)
    {
        foreach (SupportedBrowser kind in (SupportedBrowser[])[SupportedBrowser.Chrome, SupportedBrowser.ChromeHeadlessShell])
        {
            BrowserFetcher fetcher = Fetcher(kind);
            foreach (InstalledBrowser old in fetcher.GetInstalledBrowsers().Where(item => item.BuildId != buildId))
            {
                try
                {
                    fetcher.Uninstall(old.BuildId);
                    _logger.LogInformation("已删除旧版浏览器 {Browser} {BuildId}", kind, old.BuildId);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "删除旧版浏览器 {Browser} {BuildId} 失败", kind, old.BuildId);
                }
            }
        }
    }

    private BrowserFetcher Fetcher(SupportedBrowser browser) =>
        new(
            new BrowserFetcherOptions
            {
                Browser = browser,
                Path    = Path.Combine(_root, CHROME_DIRECTORY)
            });
}
