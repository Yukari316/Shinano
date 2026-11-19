using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Shinano.Plugin.ImageSources.Gateway;

/// <summary>
/// 图源服务（YukariApiNext）客户端。搜索接口把上游响应原样转发并带 <c>x-yukari-upstream</c> 头，网关自身的错误不带该头；
/// 作品信息接口把上游 JSON 放在结果包装的 <c>data</c> 中；原图接口流式返回图片。地址与令牌取自 <see cref="IGatewaySettings" />，
/// 每次调用时读取，面板修改后立即生效。错误与日志不含令牌。
/// </summary>
public sealed class YukariGateway : IDisposable
{
    private const string UPSTREAM_HEADER = "x-yukari-upstream";
    private const string USER_AGENT      = "Shinano/1.0";

    // 图源服务限定检索库的查询参数，可重复；取值为搜索服务原生的编号（SauceNAO 的 index、IQDB 的 service），
    // 网关校验后换算为上游参数，不合法时返回 400 且不请求上游。
    private const string DATABASE_PARAMETER = "db";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(100);

    // IQDB 高负载时先返回响应头，再长时间推送排队进度后才输出结果（2026-11-05 实测排队达 90 秒），单独放宽超时。
    private static readonly TimeSpan IqdbTimeout = TimeSpan.FromSeconds(180);

    private readonly IGatewaySettings       _settings;
    private readonly ILogger<YukariGateway> _logger;
    private readonly HttpClient             _http     = new() { Timeout = Timeout };
    private readonly HttpClient             _iqdbHttp = new() { Timeout = IqdbTimeout };

    /// <summary>创建客户端。</summary>
    public YukariGateway(IGatewaySettings settings, ILogger<YukariGateway> logger)
    {
        _settings = settings;
        _logger   = logger;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
        _iqdbHttp.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
    }

    /// <summary>是否配置了令牌。</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(Token);

    private string? Token => _settings.Token;

    /// <summary>
    /// 上传图片以 SauceNAO 搜索，只检索 <paramref name="databases" /> 中的库（查询参数 <c>db</c> 为 index 编号）；
    /// 为 null 时检索全部库。
    /// </summary>
    public Task<GatewayResponse> SearchSauceNaoAsync(IReadOnlySet<SauceNaoIndex>? databases, byte[] image, string mediaType, CancellationToken cancellationToken) =>
        SearchAsync(_http, SearchEngine.SauceNao, DatabaseQuery(databases?.Select(database => (int)database) ?? []), image, mediaType, cancellationToken);

    /// <summary>
    /// 上传图片以 IQDB 搜索，只检索 <paramref name="sites" /> 对应的服务（查询参数 <c>db</c> 为 service 编号）；超时为 180 秒，长于其他请求的 100 秒。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sites" /> 含 IQDB 没有的图站（如 Pixiv）。</exception>
    public Task<GatewayResponse> SearchIqdbAsync(IReadOnlySet<ImageSite> sites, byte[] image, string mediaType, CancellationToken cancellationToken) =>
        SearchAsync(_iqdbHttp, SearchEngine.Iqdb, DatabaseQuery(sites.Select(site => (int)IqdbServiceOf(site))), image, mediaType, cancellationToken);

    /// <summary>上传图片以 trace.moe 搜索。</summary>
    public Task<GatewayResponse> SearchTraceMoeAsync(byte[] image, string mediaType, CancellationToken cancellationToken) =>
        SearchAsync(_http, SearchEngine.TraceMoe, string.Empty, image, mediaType, cancellationToken);

    private async Task<GatewayResponse> SearchAsync(HttpClient http, SearchEngine engine, string query, byte[] image, string mediaType, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, $"search/{PathOf(engine)}{query}");
        request.Content = new ByteArrayContent(image);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            GatewayOutcome outcome = response.Headers.Contains(UPSTREAM_HEADER) ? GatewayOutcome.Upstream
                : response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? GatewayOutcome.Unauthorized
                : response.StatusCode == HttpStatusCode.ServiceUnavailable ? GatewayOutcome.Unconfigured
                : GatewayOutcome.Failed;
            return new GatewayResponse
            {
                Outcome    = outcome,
                StatusCode = (int)response.StatusCode,
                Body       = body
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new GatewayResponse
            {
                Outcome = GatewayOutcome.Failed,
                Body    = $"无法访问图源服务：{exception.Message}"
            };
        }
    }

    /// <summary>查询作品信息；作品不存在时 <see cref="WorkLookup.NotFound" /> 为 true，其他失败只记录日志。</summary>
    public async Task<WorkLookup> GetWorkAsync(SourceRef source, CancellationToken cancellationToken)
    {
        string path = source.Site == ImageSite.Pixiv
            ? $"pixiv/illust?pid={Uri.EscapeDataString(source.Id)}"
            : $"{PathOf(source.Site)}/post?id={Uri.EscapeDataString(source.Id)}";
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, path);
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return new WorkLookup { NotFound = true };

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("查询 {Site} 作品 {Id} 失败：HTTP {Status}", source.Site, source.Id, (int)response.StatusCode);
                return new WorkLookup();
            }

            // 结果包装的 data 为上游 JSON；旧版网关会把它序列化为字符串。
            JsonNode? data = JsonNode.Parse(body)?["data"];
            if (data is JsonValue text && text.GetValueKind() == JsonValueKind.String) data = JsonNode.Parse(text.GetValue<string>());
            return new WorkLookup { Data = data };
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("查询 {Site} 作品 {Id} 失败：{Error}", source.Site, source.Id, exception.Message);
            return new WorkLookup();
        }
    }

    /// <summary>打开原图下载，只读取响应头；失败时返回 null。调用方负责释放响应。</summary>
    public async Task<HttpResponseMessage?> OpenOriginalAsync(SourceRef source, CancellationToken cancellationToken)
    {
        string path = source.Site == ImageSite.Pixiv
            ? $"pixiv/{Uri.EscapeDataString(source.Id)}/{source.Page ?? 0}"
            : $"{PathOf(source.Site)}/{Uri.EscapeDataString(source.Id)}";
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, path);
        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode) return response;

            _logger.LogWarning("下载 {Site} 作品 {Id} 的原图失败：HTTP {Status}", source.Site, source.Id, (int)response.StatusCode);
            response.Dispose();
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("下载 {Site} 作品 {Id} 的原图失败：{Error}", source.Site, source.Id, exception.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
        _iqdbHttp.Dispose();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        string baseUrl = _settings.BaseUrl.TrimEnd('/');
        HttpRequestMessage request = new(method, $"{baseUrl}/{path}");
        if (Token is { Length: > 0 } token) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    // 同名参数重复，如 ?db=5&db=9；按数值排序，便于在日志中比对。
    private static string DatabaseQuery(IEnumerable<int> values) =>
        values.Order().Select(value => $"{DATABASE_PARAMETER}={value.ToString(CultureInfo.InvariantCulture)}").ToArray() is { Length: > 0 } pairs
            ? "?" + string.Join('&', pairs)
            : string.Empty;

    private static IqdbService IqdbServiceOf(ImageSite site) =>
        site switch
        {
            ImageSite.Danbooru  => IqdbService.Danbooru,
            ImageSite.Konachan  => IqdbService.Konachan,
            ImageSite.Yandere   => IqdbService.Yandere,
            ImageSite.Gelbooru  => IqdbService.Gelbooru,
            ImageSite.Sankaku   => IqdbService.Sankaku,
            ImageSite.Eshuushuu => IqdbService.Eshuushuu,
            _                   => throw new ArgumentOutOfRangeException(nameof(site), site, "IQDB 没有该图站")
        };

    private static string PathOf(SearchEngine engine) =>
        engine switch
        {
            SearchEngine.SauceNao => "saucenao",
            SearchEngine.Iqdb     => "iqdb",
            SearchEngine.TraceMoe => "tracemoe",
            _                     => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
        };

    private static string PathOf(ImageSite site) =>
        site switch
        {
            ImageSite.Danbooru  => "danbooru",
            ImageSite.Yandere   => "yandere",
            ImageSite.Gelbooru  => "gelbooru",
            ImageSite.Sankaku   => "sankaku",
            ImageSite.Eshuushuu => "eshuushuu",
            _                   => throw new ArgumentOutOfRangeException(nameof(site), site, "图源服务不转发该图站")
        };
}

/// <summary>iqdb.org 的服务号（表单字段 <c>service[]</c> 的取值），只列出 <see cref="ImageSite" /> 中有的图站。</summary>
internal enum IqdbService
{
    /// <summary>Danbooru。</summary>
    Danbooru = 1,

    /// <summary>Konachan。</summary>
    Konachan = 2,

    /// <summary>Yande.re。</summary>
    Yandere = 3,

    /// <summary>Gelbooru。</summary>
    Gelbooru = 4,

    /// <summary>Sankaku Channel。</summary>
    Sankaku = 5,

    /// <summary>e-shuushuu。</summary>
    Eshuushuu = 6
}
