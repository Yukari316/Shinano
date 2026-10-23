using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Shinano.Monitor;

/// <summary>
/// 通过公开接口探测各服务，每项超时 5 秒。失败只归类，不保留原始异常文本，避免地址与凭据进入记录。
/// </summary>
public sealed class ServiceProber : IDisposable
{
    /// <summary>每项探测的超时。</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private const string READINESS_PATH = "/healthz";
    private const string HEALTH_PATH    = "/health";
    private const string LOGIN_INFO_API = "api/get_login_info";
    private const int    POSTGRES_PORT  = 5432;

    private readonly MonitorSettings _settings;
    private readonly string          _hostBaseUrl;
    private readonly HttpClient      _hostClient;
    private readonly HttpClient      _client;
    private readonly TimeProvider    _time;

    /// <summary>创建探测器；主进程地址按部署配置中的面板监听规则解析。</summary>
    public ServiceProber(MonitorSettings settings, TimeProvider time)
    {
        _settings = settings;
        _time     = time;
        ListenEndpoint endpoint = ListenEndpoint.Resolve(settings.HostWeb, MonitorSettings.DEFAULT_HOST_PORT, settings.DataDirectory);
        _hostBaseUrl = endpoint.BaseUrl;
        string? thumbprint = endpoint.Thumbprint;
        endpoint.Certificate?.Dispose();
        _hostClient = CreateClient(thumbprint);
        _client     = CreateClient(null);
    }

    /// <summary>探测一项服务。</summary>
    public async Task<ProbeRecord> ProbeAsync(MonitoredService service, CancellationToken cancellationToken)
    {
        DateTimeOffset start = _time.GetUtcNow();
        Stopwatch stopwatch = Stopwatch.StartNew();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        Outcome outcome;
        try
        {
            outcome = service switch
            {
                MonitoredService.Host          => await ProbeHostAsync(timeout.Token),
                MonitoredService.Database      => await ProbeDatabaseAsync(timeout.Token),
                MonitoredService.ObjectStorage => await ProbeObjectStorageAsync(timeout.Token),
                _                              => await ProbeBotAsync(timeout.Token)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            outcome = Outcome.Failed(ProbeFailure.Timeout);
        }
        catch (Exception exception) when (exception is HttpRequestException or SocketException or IOException)
        {
            outcome = Outcome.Failed(ProbeFailure.ConnectionFailed);
        }
        catch (NpgsqlException exception)
        {
            outcome = Outcome.Failed(Classify(exception));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            outcome = Outcome.Failed(ProbeFailure.Error);
        }

        return new ProbeRecord
        {
            Time       = start,
            Service    = service,
            Level      = outcome.Level,
            LatencyMs  = (int)stopwatch.ElapsedMilliseconds,
            Failure    = outcome.Failure,
            StatusCode = outcome.StatusCode
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _hostClient.Dispose();
        _client.Dispose();
    }

    // 主进程就绪接口：启动中为降级，故障模式为停止，就绪后采用其整体等级。
    private async Task<Outcome> ProbeHostAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _hostClient.GetAsync(_hostBaseUrl + READINESS_PATH, cancellationToken);
        if (!response.IsSuccessStatusCode) return Outcome.Http(response.StatusCode);

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        string? phase   = document.RootElement.GetProperty("phase").GetString();
        string? overall = document.RootElement.GetProperty("overall").GetString();
        return phase switch
        {
            "FaultMode" => Outcome.Failed(ProbeFailure.FaultMode),
            "Starting"  => new Outcome { Level = HealthLevel.Degraded, Failure = ProbeFailure.Starting },
            _           => Enum.TryParse(overall, out HealthLevel level) && level != HealthLevel.Normal
                ? new Outcome { Level = level, Failure = ProbeFailure.Unhealthy }
                : Outcome.Healthy
        };
    }

    private async Task<Outcome> ProbeDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_settings.DatabaseDsn is not { } dsn) return Outcome.Failed(ProbeFailure.NotConfigured);

        NpgsqlConnectionStringBuilder builder = ParseDsn(dsn);
        builder.Timeout = (int)Timeout.TotalSeconds;
        builder.Pooling = false;
        await using NpgsqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = new("select 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
        return Outcome.Healthy;
    }

    private async Task<Outcome> ProbeObjectStorageAsync(CancellationToken cancellationToken)
    {
        if (_settings.ObjectStorageEndpoint is not { } endpoint) return Outcome.Failed(ProbeFailure.NotConfigured);

        using HttpResponseMessage response = await _client.GetAsync(endpoint.TrimEnd('/') + HEALTH_PATH, cancellationToken);
        return response.IsSuccessStatusCode ? Outcome.Healthy : Outcome.Http(response.StatusCode);
    }

    // Milky 的 API 需要 AccessToken；能取得登录信息说明 LLBot 在线且凭据有效。
    private async Task<Outcome> ProbeBotAsync(CancellationToken cancellationToken)
    {
        string prefix = _settings.LlBotPrefix.Trim('/');
        string url = $"http://{_settings.LlBotHost}:{_settings.LlBotPort}/{(prefix.Length > 0 ? prefix + "/" : string.Empty)}{LOGIN_INFO_API}";
        using HttpRequestMessage request = new(HttpMethod.Post, url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        if (_settings.LlBotAccessToken is { } token) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Outcome.Failed(ProbeFailure.AuthenticationFailed),
            _ when response.IsSuccessStatusCode                    => Outcome.Healthy,
            _                                                      => Outcome.Http(response.StatusCode)
        };
    }

    private static ProbeFailure Classify(NpgsqlException exception) =>
        exception switch
        {
            // 28P01：密码错误；28000：认证方式或角色无效。
            PostgresException { SqlState: "28P01" or "28000" } => ProbeFailure.AuthenticationFailed,
            PostgresException                                  => ProbeFailure.Error,
            _ when exception.InnerException is TimeoutException => ProbeFailure.Timeout,
            _                                                  => ProbeFailure.ConnectionFailed
        };

    // DSN 格式与主进程相同：postgres:// 或 postgresql:// URI，或 Npgsql 键值对字符串。
    private static NpgsqlConnectionStringBuilder ParseDsn(string dsn)
    {
        if (!dsn.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !dsn.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return new NpgsqlConnectionStringBuilder(dsn);

        Uri uri = new(dsn);
        string[] userInfo = uri.UserInfo.Split(':', 2);
        return new NpgsqlConnectionStringBuilder
        {
            Host     = uri.Host,
            Port     = uri.IsDefaultPort || uri.Port <= 0 ? POSTGRES_PORT : uri.Port,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'))
        };
    }

    // 主进程与监控在同一台机器，按指纹只信任面板证书，不依赖系统信任链与主机名。
    private static HttpClient CreateClient(string? thumbprint)
    {
        SocketsHttpHandler handler = new();
        if (thumbprint is not null)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                certificate is not null
                && string.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256), thumbprint, StringComparison.OrdinalIgnoreCase);
        return new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    private sealed class Outcome
    {
        public static Outcome Healthy { get; } = new() { Level = HealthLevel.Normal };

        public required HealthLevel Level { get; init; }

        public ProbeFailure? Failure { get; init; }

        public int? StatusCode { get; init; }

        public static Outcome Failed(ProbeFailure failure) =>
            new()
            {
                Level   = HealthLevel.Stopped,
                Failure = failure
            };

        public static Outcome Http(HttpStatusCode status) =>
            new()
            {
                Level      = HealthLevel.Stopped,
                Failure    = ProbeFailure.HttpStatus,
                StatusCode = (int)status
            };
    }
}
