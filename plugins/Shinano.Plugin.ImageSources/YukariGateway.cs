using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Shinano.Plugin.ImageSources.Models;

namespace Shinano.Plugin.ImageSources;

/// <summary>
/// 图源服务（YukariApiNext）客户端。搜索接口把上游响应原样转发并带 <c>x-yukari-upstream</c> 头，网关自身的错误不带该头；
/// 作品信息接口把上游 JSON 放在结果包装的 <c>data</c> 中；原图接口流式返回图片。地址与令牌取自 <see cref="IGatewaySettings" />，
/// 每次调用时读取，面板修改后立即生效。错误与日志不含令牌。
/// </summary>
public sealed class YukariGateway : IDisposable
{
    private const string UPSTREAM_HEADER   = "x-yukari-upstream";
    private const string USER_AGENT        = "Shinano/1.0";
    private const string SAUCENAO_DATABASE = "db";
    private const string IQDB_SERVICE      = "service";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(100);

    private readonly IGatewaySettings       _settings;
    private readonly ILogger<YukariGateway> _logger;
    private readonly HttpClient             _http = new() { Timeout = Timeout };

    /// <summary>创建客户端。</summary>
    public YukariGateway(IGatewaySettings settings, ILogger<YukariGateway> logger)
    {
        _settings = settings;
        _logger   = logger;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
    }

    /// <summary>是否配置了令牌。</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(Token);

    private string? Token => _settings.Token;

    /// <summary>上传图片以 SauceNAO 搜索，只检索 <paramref name="databases" /> 中的库（查询参数 <c>db</c>）。</summary>
    public Task<GatewayResponse> SearchSauceNaoAsync(IReadOnlySet<SauceNaoIndex> databases, byte[] image, string mediaType, CancellationToken cancellationToken) =>
        SearchAsync(SearchEngine.SauceNao, Query(SAUCENAO_DATABASE, databases.Select(database => (int)database)), image, mediaType, cancellationToken);

    /// <summary>上传图片以 IQDB 搜索，只检索 <paramref name="sites" /> 对应的服务（查询参数 <c>service</c>）。</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sites" /> 含 IQDB 没有的图站（如 Pixiv）。</exception>
    public Task<GatewayResponse> SearchIqdbAsync(IReadOnlySet<ImageSite> sites, byte[] image, string mediaType, CancellationToken cancellationToken) =>
        SearchAsync(SearchEngine.Iqdb, Query(IQDB_SERVICE, sites.Select(site => (int)IqdbServiceOf(site))), image, mediaType, cancellationToken);

    /// <summary>上传图片以 trace.moe 搜索。</summary>
    public Task<GatewayResponse> SearchTraceMoeAsync(byte[] image, string mediaType, CancellationToken cancellationToken) =>
        SearchAsync(SearchEngine.TraceMoe, string.Empty, image, mediaType, cancellationToken);

    private async Task<GatewayResponse> SearchAsync(SearchEngine engine, string query, byte[] image, string mediaType, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, $"search/{PathOf(engine)}{query}");
        request.Content = new ByteArrayContent(image);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
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
    public void Dispose() => _http.Dispose();

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        string baseUrl = _settings.BaseUrl.TrimEnd('/');
        HttpRequestMessage request = new(method, $"{baseUrl}/{path}");
        if (Token is { Length: > 0 } token) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    // 同名参数重复，如 ?db=5&db=9；按数值排序，便于在日志中比对。
    private static string Query(string name, IEnumerable<int> values) =>
        values.Order().Select(value => $"{name}={value.ToString(CultureInfo.InvariantCulture)}").ToArray() is { Length: > 0 } pairs
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
