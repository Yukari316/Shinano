using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Shinano.Plugin.NB2Bridge.Runtime;

/// <summary>调用 NB2 内控制插件的 API（<c>/shinano/</c>，只在 127.0.0.1）。每次启动 NB2 对应一个实例。</summary>
public sealed class Nb2ControlClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;

    /// <summary>创建客户端。</summary>
    public Nb2ControlClient(int port, string token)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/shinano/"),
            Timeout     = RequestTimeout
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>托管插件的实时状态。</summary>
    /// <exception cref="HttpRequestException">NB2 不可达或拒绝请求。</exception>
    public async Task<IReadOnlyList<Nb2RemotePlugin>> PluginsAsync(CancellationToken cancellationToken) =>
        await _http.GetFromJsonAsync<List<Nb2RemotePlugin>>("plugins", JsonOptions, cancellationToken) ?? [];

    /// <summary>NB2 进程的运行环境版本。</summary>
    /// <exception cref="HttpRequestException">NB2 不可达或拒绝请求。</exception>
    /// <exception cref="JsonException">响应缺少版本字段。</exception>
    public async Task<Nb2RemoteStatus> StatusAsync(CancellationToken cancellationToken) =>
        await _http.GetFromJsonAsync<Nb2RemoteStatus>("status", JsonOptions, cancellationToken) ?? throw new JsonException("NB2 状态响应为空");

    /// <summary>推送完整的禁用集合。</summary>
    /// <exception cref="HttpRequestException">NB2 不可达或拒绝请求。</exception>
    public async Task SetDisabledAsync(IReadOnlyCollection<string> modules, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.PutAsJsonAsync("plugins/disabled", new { disabled = modules }, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>请求 NB2 正常关停；只确认请求被接受，不等待进程退出。</summary>
    /// <exception cref="HttpRequestException">NB2 不可达或拒绝请求。</exception>
    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.PostAsync("shutdown", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}
