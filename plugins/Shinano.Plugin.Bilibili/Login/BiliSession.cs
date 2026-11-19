using System.Net;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using Shinano.Framework.Plugins;
using Shinano.Framework.Rendering;
using Shinano.Plugin.Bilibili.Persistence;

namespace Shinano.Plugin.Bilibili.Login;

/// <summary>
/// B站登录状态与浏览器维护。浏览器用户目录是登录状态的唯一来源：登录时把扫码得到的 Cookie 与刷新令牌写入浏览器，
/// 维护时打开 B站首页让网页前端自行检查与刷新登录、生成风控 Cookie，再把 Cookie 与浏览器身份导出给 <see cref="BiliHttp" />。
/// 维护、登录与退出串行执行。风控暂停与登录失效的通知各只发一次。线程安全。
/// </summary>
public sealed class BiliSession
{
    /// <summary>遇到风控后暂停动态读取的时长。</summary>
    public static readonly TimeSpan RiskPause = TimeSpan.FromHours(1);

    private const string HOME                 = "https://www.bilibili.com/";
    private const string REFRESH_TOKEN_KEY    = "ac_time_value";
    private const string CSRF_COOKIE          = "bili_jct";
    private const string COOKIE_DOMAIN_SUFFIX = "bilibili.com";
    private const int    NAVIGATION_TIMEOUT   = 45_000;

    // 首页加载后网页前端会请求 cookie/info 检查是否需要刷新，留出时间让刷新与风控 Cookie 的生成完成。
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(5);

    private static readonly string[] CookieUrls =
    [
        "https://www.bilibili.com/",
        "https://api.bilibili.com/",
        "https://passport.bilibili.com/",
        "https://t.bilibili.com/",
        "https://space.bilibili.com/",
        "https://api.live.bilibili.com/"
    ];

    private readonly IPluginInitializationContext _context;
    private readonly BiliHttp                     _http;
    private readonly BiliStore                    _store;
    private readonly TimeProvider                 _time;
    private readonly ILogger<BiliSession>         _logger;
    private readonly SemaphoreSlim                _gate = new(1, 1);
    private readonly Lock                         _lock = new();
    private          BiliLoginState               _state = BiliLoginState.Unknown;
    private          BiliAccount?                 _account;
    private          DateTimeOffset?              _riskUntil;

    /// <summary>创建会话。</summary>
    public BiliSession(IPluginInitializationContext context, BiliHttp http, BiliStore store, TimeProvider time, ILogger<BiliSession> logger)
    {
        _context = context;
        _http    = http;
        _store   = store;
        _time    = time;
        _logger  = logger;
    }

    /// <summary>登录状态。</summary>
    public BiliLoginState State
    {
        get
        {
            lock (_lock) return _state;
        }
    }

    /// <summary>当前部署的 B站账号：已登录时为该账号，登录失效时为失效前的账号，未登录或已主动退出时为 null。</summary>
    public BiliAccount? Account
    {
        get
        {
            lock (_lock) return _account;
        }
    }

    /// <summary>风控暂停结束的时间；不在风控暂停中时为 null。暂停结束后仍保留，直到下一次读取成功。</summary>
    public DateTimeOffset? RiskUntil
    {
        get
        {
            lock (_lock) return _riskUntil;
        }
    }

    /// <summary>
    /// 用浏览器打开 B站首页，由网页前端检查与刷新登录，然后导出 Cookie 与浏览器身份并查询登录状态。
    /// 浏览器或接口出错时只记录日志，保留原登录状态。
    /// </summary>
    public async Task MaintainAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using (IBrowserSession session = await _context.Browser.OpenAsync(cancellationToken))
            {
                await session.Page.GoToAsync(HOME, NAVIGATION_TIMEOUT, [WaitUntilNavigation.Networkidle2]);
                await Task.Delay(SettleDelay, _time, cancellationToken);
                await ExportAsync(session);
            }

            BiliAccount? account = await _http.NavAsync(cancellationToken);
            await UpdateLoginAsync(account);
            _logger.LogInformation("B站浏览器维护完成：{State}{Account}", State, account is null ? "" : $"，账号 {account.Name}（{account.Uid}）");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "B站浏览器维护失败，保留原登录状态 {State}", State);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 把扫码成功下发的 Cookie 与刷新令牌写入浏览器，重新加载首页后导出 Cookie 并确认登录。
    /// </summary>
    /// <returns>登录确认成功时为 <see cref="BiliQrState.Succeeded" />，否则为 <see cref="BiliQrState.Failed" />。</returns>
    public async Task<BiliQrState> CompleteLoginAsync(BiliQrPoll poll, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using (IBrowserSession session = await _context.Browser.OpenAsync(cancellationToken))
            {
                IPage page = session.Page;
                _logger.LogInformation("写入扫码下发的 Cookie：{Names}", string.Join("、", poll.Cookies.Select(cookie => $"{cookie.Name}@{cookie.Domain}")));
                await page.SetCookieAsync([.. poll.Cookies.Select(ToBrowserCookie)]);
                await page.GoToAsync(HOME, NAVIGATION_TIMEOUT, [WaitUntilNavigation.Networkidle2]);
                if (poll.RefreshToken.Length > 0)
                    await page.EvaluateFunctionAsync("(key, value) => localStorage.setItem(key, value)", REFRESH_TOKEN_KEY, poll.RefreshToken);
                await page.ReloadAsync(NAVIGATION_TIMEOUT, [WaitUntilNavigation.Networkidle2]);
                await Task.Delay(SettleDelay, _time, cancellationToken);
                await ExportAsync(session);
            }

            BiliAccount? account = await _http.NavAsync(cancellationToken);
            if (account is null)
            {
                _logger.LogError("B站扫码登录成功，但写入浏览器后查询仍为未登录；导出的 Cookie：{Names}", CookieNames());
                return BiliQrState.Failed;
            }

            await UpdateLoginAsync(account);
            _logger.LogInformation("B站扫码登录成功：{Name}（{Uid}）", account.Name, account.Uid);
            return BiliQrState.Succeeded;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "B站扫码登录写入浏览器失败");
            return BiliQrState.Failed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 退出当前账号使其在服务端失效，再清空浏览器用户目录与当前账号记录；退出请求失败时仍清空并记录日志。
    /// 该账号的关注列表与推送设置保留，重新登录该账号时恢复。
    /// </summary>
    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            BiliAccount? account = Account;
            if (account is not null)
            {
                try
                {
                    await _http.LogoutAsync(cancellationToken);
                    _logger.LogInformation("已退出 B站账号 {Name}（{Uid}）", account.Name, account.Uid);
                }
                catch (Exception exception) when (exception is BiliApiException or HttpRequestException or TaskCanceledException)
                {
                    _logger.LogWarning(exception, "退出 B站账号 {Uid} 失败，仍清空浏览器登录数据", account.Uid);
                }
            }

            await _context.Browser.ClearProfileAsync(cancellationToken);
            if (account is not null) await _store.DeleteAccountAsync(account.Uid);
            _http.UseCredentials(null);
            lock (_lock)
            {
                _state   = BiliLoginState.LoggedOut;
                _account = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>读取遇到风控：暂停动态读取 1 小时；进入暂停时通知 Super User 一次。</summary>
    public void ReportRisk(BiliApiException exception)
    {
        bool started;
        DateTimeOffset until = _time.GetUtcNow() + RiskPause;
        lock (_lock)
        {
            started    = _riskUntil is null;
            _riskUntil = until;
        }

        _logger.LogWarning("B站接口风控（{Code}），暂停动态读取至 {Until:O}", exception.Code, until);
        if (started)
            _context.Notices.NotifySuperUsers(
                new SuperUserNotice
                {
                    Level  = CardLevel.Warning,
                    Title  = "B站接口风控",
                    Code   = "BILI-RISK",
                    Cause  = $"B站接口返回风控码 {exception.Code}",
                    Impact = "B站动态推送暂停 1 小时，之后刷新登录信息再试"
                });
    }

    /// <summary>风控暂停后读取恢复成功：清除暂停并通知 Super User 一次。</summary>
    public void ReportRecovered()
    {
        lock (_lock)
        {
            if (_riskUntil is null) return;
            _riskUntil = null;
        }

        _logger.LogInformation("B站接口风控已解除，恢复动态推送");
        _context.Notices.NotifySuperUsers(
            new SuperUserNotice
            {
                Level  = CardLevel.Normal,
                Title  = "B站接口风控已解除",
                Code   = "BILI-RISK-RECOVERED",
                Impact = "B站动态推送已恢复"
            });
    }

    // 登录成功时保存为当前账号。未登录时，有当前账号记录即为登录失效：保留该账号（直播照常按其关注列表推送），
    // 只在第一次发现失效时通知，失效标记存于数据库，进程重启后不重复通知。
    private async Task UpdateLoginAsync(BiliAccount? account)
    {
        if (account is not null)
        {
            await _store.SaveAccountAsync(account);
            SetState(BiliLoginState.LoggedIn, account);
            return;
        }

        if (await _store.AccountAsync() is not { } stored)
        {
            SetState(BiliLoginState.LoggedOut, null);
            return;
        }

        SetState(
            BiliLoginState.Expired,
            new BiliAccount
            {
                Uid  = stored.Uid,
                Name = stored.Name
            });
        if (stored.Expired) return;

        await _store.MarkExpiredAsync(stored.Uid);
        _logger.LogWarning("B站账号 {Name}（{Uid}）登录已失效", stored.Name, stored.Uid);
        _context.Notices.NotifySuperUsers(
            new SuperUserNotice
            {
                Level  = CardLevel.Error,
                Title  = "B站登录已失效",
                Code   = "BILI-LOGIN-EXPIRED",
                Cause  = "B站返回未登录",
                Impact = "B站动态推送暂停，直播提醒照常；重新扫码登录后恢复"
            });
    }

    private void SetState(BiliLoginState state, BiliAccount? account)
    {
        lock (_lock)
        {
            _state   = state;
            _account = account;
        }
    }

    // 只记录名称，不记录值。
    private string CookieNames() =>
        string.Join("、", (_http.Credentials?.CookieHeader ?? "").Split("; ", StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=')[0]));

    private async Task ExportAsync(IBrowserSession session)
    {
        CookieParam[] cookies = await session.Page.GetCookiesAsync(CookieUrls);
        Dictionary<string, string> values = [];
        foreach (CookieParam cookie in cookies.Where(cookie => (cookie.Domain ?? "").TrimStart('.').EndsWith(COOKIE_DOMAIN_SUFFIX, StringComparison.Ordinal)))
            values.TryAdd(cookie.Name, cookie.Value);

        _http.UseCredentials(
            new BiliCredentials
            {
                CookieHeader = string.Join("; ", values.Select(pair => $"{pair.Key}={pair.Value}")),
                Csrf         = values.GetValueOrDefault(CSRF_COOKIE, ""),
                Identity     = session.Identity
            });
    }

    /// <summary>
    /// 把扫码下发的 Cookie 转为写入浏览器的参数。域统一加前导点：.NET 解析 <c>Domain=bilibili.com</c> 后的域不带点，
    /// 原样交给 CDP 会被当作只属于 <c>bilibili.com</c> 主机的 Cookie，<c>www</c>、<c>api</c> 等子域都收不到，登录无效。
    /// </summary>
    public static CookieParam ToBrowserCookie(Cookie cookie) =>
        new()
        {
            Name     = cookie.Name,
            Value    = cookie.Value,
            Domain   = "." + cookie.Domain.TrimStart('.'),
            Path     = string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
            Expires  = cookie.Expires == DateTime.MinValue ? null : new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds(),
            HttpOnly = cookie.HttpOnly,
            Secure   = cookie.Secure
        };
}
