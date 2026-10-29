using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Shinano.Tool.Run;

/// <summary>轮询宿主就绪接口，判断启动流程是否结束。</summary>
public sealed class ReadinessProbe : IDisposable
{
    /// <summary>新进程完成启动的时限。</summary>
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan PollInterval   = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    private readonly HostEndpoint _endpoint;
    private readonly HttpClient   _http;

    /// <summary>创建探测。</summary>
    public ReadinessProbe(HostEndpoint endpoint)
    {
        _endpoint = endpoint;
        _http     = CreateClient(endpoint.Thumbprint);
    }

    /// <summary>
    /// 等待启动流程结束。
    /// </summary>
    /// <param name="process">被等待的宿主进程；为 null 时（服务模式）只按接口判断。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否在 <see cref="ReadyTimeout" /> 内就绪；进程先退出时为 false。</returns>
    public async Task<bool> WaitReadyAsync(Process? process, CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + ReadyTimeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (process is { HasExited: true }) return false;
            if (await ReadPhaseAsync(cancellationToken) is { } phase && HostContract.FinishedPhases.Contains(phase)) return true;
            try
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    // 连接失败与非 2xx 响应都视为尚未就绪。
    private async Task<string?> ReadPhaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using HttpResponseMessage response = await _http.GetAsync(_endpoint.ReadinessUrl, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;

            await using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
            return document.RootElement.TryGetProperty(HostContract.PHASE_PROPERTY, out JsonElement phase) ? phase.GetString() : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    // 工具与宿主在同一台机器，按指纹只信任面板配置的证书，不依赖系统信任链与主机名。
    private static HttpClient CreateClient(string? thumbprint)
    {
        HttpClientHandler handler = new();
        if (thumbprint is not null)
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null
                && string.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256), thumbprint, StringComparison.OrdinalIgnoreCase);
        return new HttpClient(handler);
    }
}
