using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.ImageSearch.Models;

namespace Shinano.Plugin.ImageSearch;

/// <summary>
/// 图源服务（YukariApiNext）客户端。搜索接口把上游响应原样转发并带 <c>x-yukari-upstream</c> 头，网关自身的错误不带该头；
/// 作品信息接口把上游 JSON 放在结果包装的 <c>data</c> 中；原图接口流式返回图片。地址取插件配置，令牌取插件机密，
/// 每次调用时读取，面板修改后立即生效。错误与日志不含令牌。
/// </summary>
public sealed class YukariGateway : IDisposable
{
    private const string UPSTREAM_HEADER = "x-yukari-upstream";
    private const string USER_AGENT      = "Shinano/1.0";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(100);

    private readonly IPluginInitializationContext _context;
    private readonly ILogger<YukariGateway>       _logger;
    private readonly HttpClient                   _http = new() { Timeout = Timeout };

    /// <summary>创建客户端。</summary>
    public YukariGateway(IPluginInitializationContext context, ILogger<YukariGateway> logger)
    {
        _context = context;
        _logger  = logger;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
    }

    /// <summary>插件机密中是否配置了令牌。</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(Token);

    private string? Token => _context.Secrets<ImageSearchSecrets>().Value.GatewayToken;

    /// <summary>上传图片搜索。</summary>
    public async Task<GatewayResponse> SearchAsync(SearchEngine engine, byte[] image, string mediaType, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, $"search/{PathOf(engine)}");
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
        string baseUrl = _context.Config<ImageSearchConfig>().Value.GatewayUrl.TrimEnd('/');
        HttpRequestMessage request = new(method, $"{baseUrl}/{path}");
        if (Token is { Length: > 0 } token) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

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
