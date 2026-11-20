using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shinano.Plugin.Bilibili.Feed;
using Shinano.Plugin.Bilibili.Login;

namespace Shinano.Plugin.Bilibili;

/// <summary>
/// B站 Web 接口的普通 HTTP 读取。每个请求带最近一次从浏览器导出的 Cookie 与浏览器的 UA、Client Hints，不做签名与指纹；
/// 登录与 Cookie 刷新由浏览器中的网页前端完成。线程安全。
/// </summary>
public sealed class BiliHttp : IDisposable
{
    private const string API              = "https://api.bilibili.com";
    private const string PASSPORT         = "https://passport.bilibili.com";
    private const string LIVE_API         = "https://api.live.bilibili.com";
    private const string REFERER          = "https://www.bilibili.com/";
    private const string FEED_FEATURES    = "itemOpusStyle";
    private const int    FOLLOWINGS_SIZE  = 50;
    private const int    MAX_FOLLOW_PAGES = 100;
    private const int    QR_SUCCESS       = 0;
    private const int    QR_EXPIRED       = 86038;
    private const int    QR_SCANNED       = 86090;
    private const int    QR_WAITING       = 86101;
    private const int    LIVE_ON          = 1;
    private const long   MAX_IMAGE_BYTES  = 30L * 1024 * 1024;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient        _client;
    private readonly ILogger<BiliHttp> _logger;
    private          BiliCredentials?  _credentials;

    /// <summary>创建客户端；Cookie 由调用方经 <see cref="UseCredentials" /> 提供，不使用自动 Cookie 容器。</summary>
    public BiliHttp(ILogger<BiliHttp> logger)
    {
        _client = new HttpClient(new SocketsHttpHandler
        {
            UseCookies             = false,
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = RequestTimeout
        };
        _logger = logger;
    }

    /// <summary>当前使用的 Cookie 与浏览器身份；还没从浏览器导出过时为 null。</summary>
    public BiliCredentials? Credentials => Volatile.Read(ref _credentials);

    /// <summary>替换之后请求使用的 Cookie 与浏览器身份。</summary>
    public void UseCredentials(BiliCredentials? credentials) => Volatile.Write(ref _credentials, credentials);

    /// <summary>当前登录的账号；未登录时为 null。</summary>
    public async Task<BiliAccount?> NavAsync(CancellationToken cancellationToken)
    {
        try
        {
            JsonElement data = await GetAsync($"{API}/x/web-interface/nav", cancellationToken);
            return data.GetProperty("isLogin").GetBoolean()
                ? new BiliAccount
                {
                    Uid  = data.GetProperty("mid").GetInt64(),
                    Name = data.GetProperty("uname").GetString() ?? ""
                }
                : null;
        }
        catch (BiliApiException exception) when (exception.IsNotLoggedIn)
        {
            return null;
        }
    }

    /// <summary>账号的全部关注。</summary>
    /// <exception cref="InvalidDataException">读到的数量少于接口报告的总数（中途被限流、翻页提前结束）。</exception>
    public async Task<IReadOnlyList<BiliFollowing>> FollowingsAsync(long uid, CancellationToken cancellationToken)
    {
        List<BiliFollowing> result = [];
        int total = 0;
        for (int page = 1; page <= MAX_FOLLOW_PAGES; page++)
        {
            JsonElement data = await GetAsync($"{API}/x/relation/followings?vmid={uid}&pn={page}&ps={FOLLOWINGS_SIZE}&order=desc", cancellationToken);
            JsonElement list = data.GetProperty("list");
            int count = 0;
            foreach (JsonElement item in list.EnumerateArray())
            {
                count++;
                result.Add(new BiliFollowing
                {
                    Uid  = item.GetProperty("mid").GetInt64(),
                    Name = item.GetProperty("uname").GetString() ?? "",
                    Face = item.TryGetProperty("face", out JsonElement face) ? face.GetString() ?? "" : ""
                });
            }

            total = data.TryGetProperty("total", out JsonElement totalElement) ? totalElement.GetInt32() : 0;
            if (count < FOLLOWINGS_SIZE || result.Count >= total) break;
        }

        // 读不全时不能当作完整列表：调用方会据此把没读到的 UP 当作已取关，重置其推送设置。
        if (total > 0 && result.Count < total)
            throw new InvalidDataException($"B站关注列表只读到 {result.Count} 个，少于总数 {total}");
        return result;
    }

    /// <summary>关注流自 <paramref name="baseline" /> 以来的新动态数。</summary>
    public async Task<int> FeedUpdateCountAsync(string baseline, CancellationToken cancellationToken)
    {
        JsonElement data = await GetAsync(
            $"{API}/x/polymer/web-dynamic/v1/feed/all/update?type=all&update_baseline={Uri.EscapeDataString(baseline)}",
            cancellationToken);
        return data.GetProperty("update_num") switch
        {
            { ValueKind: JsonValueKind.Number } number => number.GetInt32(),
            { ValueKind: JsonValueKind.String } text   => int.TryParse(text.GetString(), CultureInfo.InvariantCulture, out int value) ? value : 0,
            _                                          => 0
        };
    }

    /// <summary>登录账号的关注流的一页；<paramref name="offset" /> 为 null 时为第一页。</summary>
    public async Task<BiliFeedPage> FeedAsync(string? offset, CancellationToken cancellationToken)
    {
        string query = offset is null ? "" : $"&offset={Uri.EscapeDataString(offset)}";
        return BiliDynamicParser.ParsePage(
            await GetAsync($"{API}/x/polymer/web-dynamic/v1/feed/all?type=all&platform=web&features={FEED_FEATURES}{query}", cancellationToken));
    }

    /// <summary>一个 UP 的动态空间第一页（含置顶）。</summary>
    public async Task<BiliFeedPage> SpaceAsync(long uid, CancellationToken cancellationToken) =>
        BiliDynamicParser.ParsePage(
            await GetAsync($"{API}/x/polymer/web-dynamic/v1/feed/space?host_mid={uid}&features={FEED_FEATURES}", cancellationToken));

    /// <summary>批量查询直播状态；接口没有返回的 UID（没有直播间）不在结果中。不需要登录。</summary>
    public async Task<IReadOnlyList<BiliLiveStatus>> LiveStatusAsync(IReadOnlyCollection<long> uids, CancellationToken cancellationToken)
    {
        if (uids.Count == 0) return [];
        string query = string.Join('&', uids.Select(uid => $"uids[]={uid}"));
        JsonElement data = await GetAsync($"{LIVE_API}/room/v1/Room/get_status_info_by_uids?{query}", cancellationToken);
        if (data.ValueKind != JsonValueKind.Object) return [];

        return
        [
            .. data.EnumerateObject()
                   .Select(entry => entry.Value)
                   .Select(room =>
                   {
                       long since = room.TryGetProperty("live_time", out JsonElement time) && time.ValueKind == JsonValueKind.Number ? time.GetInt64() : 0;
                       bool live = room.GetProperty("live_status").GetInt32() == LIVE_ON;
                       return new BiliLiveStatus
                       {
                           Uid       = room.GetProperty("uid").GetInt64(),
                           RoomId    = room.GetProperty("room_id").GetInt64(),
                           Live      = live,
                           LiveSince = live && since > 0 ? DateTimeOffset.FromUnixTimeSeconds(since) : null
                       };
                   })
        ];
    }

    /// <summary>生成扫码登录的二维码内容与轮询键。不带 Cookie。</summary>
    public async Task<(string Url, string Key)> QrGenerateAsync(CancellationToken cancellationToken)
    {
        JsonElement data = await GetAsync($"{PASSPORT}/x/passport-login/web/qrcode/generate", cancellationToken, withCookies: false);
        return (data.GetProperty("url").GetString() ?? "", data.GetProperty("qrcode_key").GetString() ?? "");
    }

    /// <summary>查询扫码结果；成功时返回下发的 Cookie 与刷新令牌。不带 Cookie。</summary>
    public async Task<BiliQrPoll> QrPollAsync(string key, CancellationToken cancellationToken)
    {
        Uri uri = new($"{PASSPORT}/x/passport-login/web/qrcode/poll?qrcode_key={Uri.EscapeDataString(key)}");
        using HttpResponseMessage response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), withCookies: false, cancellationToken);
        JsonElement data = await DataAsync(response, cancellationToken);
        int code = data.GetProperty("code").GetInt32();
        switch (code)
        {
            case QR_WAITING:
                return new BiliQrPoll { State = BiliQrState.Waiting };
            case QR_SCANNED:
                return new BiliQrPoll { State = BiliQrState.Scanned };
            case QR_EXPIRED:
                return new BiliQrPoll { State = BiliQrState.Expired };
            case QR_SUCCESS:
                CookieContainer container = new();
                if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? headers))
                    foreach (string header in headers)
                        container.SetCookies(uri, header);
                return new BiliQrPoll
                {
                    State        = BiliQrState.Succeeded,
                    Cookies      = [.. container.GetAllCookies().Cast<Cookie>()],
                    RefreshToken = data.TryGetProperty("refresh_token", out JsonElement token) ? token.GetString() ?? "" : ""
                };
            default:
                _logger.LogWarning("扫码登录查询返回未知状态 {Code}", code);
                return new BiliQrPoll { State = BiliQrState.Failed };
        }
    }

    /// <summary>退出当前登录，使会话在 B站服务端失效。</summary>
    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        string csrf = Credentials?.Csrf ?? "";
        HttpRequestMessage request = new(HttpMethod.Post, $"{PASSPORT}/login/exit/v2")
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("biliCSRF", csrf)])
        };
        using HttpResponseMessage response = await SendAsync(request, withCookies: true, cancellationToken);
        await DataAsync(response, cancellationToken);
    }

    /// <summary>下载图片（头像、封面、动态图片）用于绘制卡片；超过 30 MB 时抛出。不带 Cookie 与 Referer。</summary>
    public async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (Credentials?.Identity.UserAgent is { } userAgent) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MAX_IMAGE_BYTES)
            throw new InvalidOperationException($"图片超过 {MAX_IMAGE_BYTES} 字节：{url}");

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MAX_IMAGE_BYTES) throw new InvalidOperationException($"图片超过 {MAX_IMAGE_BYTES} 字节：{url}");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private async Task<JsonElement> GetAsync(string url, CancellationToken cancellationToken, bool withCookies = true)
    {
        using HttpResponseMessage response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), withCookies, cancellationToken);
        return await DataAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool withCookies, CancellationToken cancellationToken)
    {
        using (request)
        {
            BiliCredentials? credentials = Credentials;
            if (credentials is not null)
            {
                request.Headers.TryAddWithoutValidation("User-Agent", credentials.Identity.UserAgent);
                request.Headers.TryAddWithoutValidation("sec-ch-ua", credentials.Identity.SecChUa);
                request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", credentials.Identity.SecChUaMobile);
                request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", credentials.Identity.SecChUaPlatform);
                request.Headers.TryAddWithoutValidation("Accept-Language", credentials.Identity.AcceptLanguage);
                if (withCookies && credentials.CookieHeader.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", credentials.CookieHeader);
            }

            request.Headers.Referrer = new Uri(REFERER);
            HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return response;

            int status = (int)response.StatusCode;
            response.Dispose();
            throw new BiliApiException(-status, $"HTTP {status}");
        }
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        int code = root.TryGetProperty("code", out JsonElement codeElement) && codeElement.ValueKind == JsonValueKind.Number ? codeElement.GetInt32() : 0;
        if (code != 0)
        {
            string message = root.TryGetProperty("message", out JsonElement messageElement) ? messageElement.GetString() ?? "" : "";
            throw new BiliApiException(code, message);
        }

        return root.TryGetProperty("data", out JsonElement data) ? data.Clone() : default;
    }
}

/// <summary>B站接口返回了非 0 的业务码或 HTTP 错误。</summary>
public sealed class BiliApiException : Exception
{
    private const int NOT_LOGGED_IN = -101;
    private const int RISK_CONTROL  = -352;
    private const int INTERCEPTED   = -412;

    /// <summary>创建异常。</summary>
    /// <param name="code">业务码；HTTP 层错误时为负的 HTTP 状态码。</param>
    /// <param name="message">接口返回的消息。</param>
    public BiliApiException(int code, string message) : base($"B站接口返回 {code}：{message}")
    {
        Code = code;
    }

    /// <summary>业务码；HTTP 层错误时为负的 HTTP 状态码（如 HTTP 412 为 -412）。</summary>
    public int Code { get; }

    /// <summary>是否为风控拦截（<c>-352</c> 校验失败、<c>-412</c> 或 HTTP 412 请求被拦截）。</summary>
    public bool IsRiskControl => Code is RISK_CONTROL or INTERCEPTED;

    /// <summary>是否为未登录（<c>-101</c>）。</summary>
    public bool IsNotLoggedIn => Code == NOT_LOGGED_IN;
}
