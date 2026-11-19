using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using Shinano.Host.Configuration;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Storage;

namespace Shinano.Host.Panel.HostConfig;

/// <summary>
/// 保存宿主配置前，用修改后的值实际连接对应服务。每项检查最多等待 <see cref="Timeout" />；
/// 失败原因只含地址与服务返回的错误，不含 DSN、密钥等敏感值。
/// </summary>
public sealed class HostConfigConnectionCheck : IDisposable
{
    /// <summary>单项检查的超时。</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const string LOGIN_INFO_API = "api/get_login_info";
    private const string MILKY_OK       = "ok";

    private readonly DeploymentPaths _paths;
    private readonly HttpClient      _http = new() { Timeout = Timeout };

    /// <summary>创建检查器。</summary>
    public HostConfigConnectionCheck(DeploymentPaths paths) => _paths = paths;

    /// <summary>连接数据库，并拒绝 PostgreSQL 超级用户（Bot 只能以业务角色运行）。</summary>
    public async Task<ConnectionCheck> CheckDatabaseAsync(string dsn, CancellationToken cancellationToken)
    {
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = PostgresConnectionString.Parse(dsn);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return ConnectionCheck.Failed("DSN 格式无效");
        }

        builder.Timeout = (int)Timeout.TotalSeconds;
        builder.Pooling = false;
        try
        {
            await using NpgsqlConnection connection = new(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            // SQL_NEED: 检查的是尚未启用的候选连接，在 SqlSugar 之外直接查询角色目录，与初始化工具的检查一致。
            await using NpgsqlCommand command = new("select rolsuper from pg_roles where rolname = current_user", connection);
            return await command.ExecuteScalarAsync(cancellationToken) is true
                ? ConnectionCheck.Failed($"{builder.Username} 是 PostgreSQL 超级用户，Bot 不能使用超级用户运行")
                : ConnectionCheck.Passed;
        }
        catch (NpgsqlException exception)
        {
            return ConnectionCheck.Failed($"无法连接 {builder.Host}:{builder.Port}：{exception.Message}");
        }
    }

    /// <summary>以 AccessToken 调用 Milky 登录信息接口，成功时报告连上的账号。</summary>
    public async Task<ConnectionCheck> CheckLlBotAsync(string host, int port, string prefix, string accessToken, CancellationToken cancellationToken)
    {
        string trimmed = prefix.Trim('/');
        string url = $"http://{host}:{port}/{(trimmed.Length > 0 ? trimmed + "/" : string.Empty)}{LOGIN_INFO_API}";
        using HttpRequestMessage request = new(HttpMethod.Post, url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        if (accessToken.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return ConnectionCheck.Failed($"LLBot {host}:{port} 拒绝了 AccessToken");
            if (!response.IsSuccessStatusCode)
                return ConnectionCheck.Failed($"LLBot {host}:{port} 返回 HTTP {(int)response.StatusCode}，请检查地址、端口与路径前缀");

            JsonNode? body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (body?["status"]?.GetValue<string>() != MILKY_OK)
                return ConnectionCheck.Failed($"LLBot 返回错误：{body?["message"]?.GetValue<string>() ?? "未知原因"}");

            JsonNode? data = body["data"];
            return ConnectionCheck.PassedWith($"LLBot 已连接，账号 {data?["nickname"]?.GetValue<string>()}（{data?["uin"]}）");
        }
        catch (HttpRequestException exception)
        {
            return ConnectionCheck.Failed($"无法连接 LLBot {host}:{port}：{exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ConnectionCheck.Failed($"连接 LLBot {host}:{port} 超时");
        }
        catch (JsonException)
        {
            return ConnectionCheck.Failed($"LLBot {host}:{port} 的响应不是 Milky 协议，请检查路径前缀");
        }
    }

    /// <summary>用内部地址与密钥列出 bucket；IM 可达地址只检查格式，宿主无法代替 IM 实现端访问它。</summary>
    public async Task<ConnectionCheck> CheckRustFsAsync(RustFsOptions options, CancellationToken cancellationToken)
    {
        ObjectStorage storage;
        try
        {
            storage = new ObjectStorage(options);
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException)
        {
            return ConnectionCheck.Failed($"RustFS 配置无效：{exception.Message}");
        }

        using (storage)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            try
            {
                await storage.CheckAsync(timeout.Token);
                return ConnectionCheck.Passed;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ConnectionCheck.Failed($"连接 RustFS {options.Endpoint} 超时");
            }
            // AWS SDK 的连接与认证失败分属多种异常类型，统一报告为不可用。
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return ConnectionCheck.Failed($"无法访问 RustFS {options.Endpoint}：{exception.Message}");
            }
        }
    }

    /// <summary>加载 PFX 证书并确认含私钥；相对路径基于部署数据目录，与宿主和监控加载证书的方式一致。</summary>
    public ConnectionCheck CheckCertificate(string path, string? password)
    {
        string fullPath = Path.GetFullPath(path, _paths.DataDirectory);
        if (!File.Exists(fullPath)) return ConnectionCheck.Failed($"证书文件不存在：{fullPath}");

        try
        {
            using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(fullPath, password);
            return certificate.HasPrivateKey ? ConnectionCheck.Passed : ConnectionCheck.Failed($"证书 {fullPath} 不含私钥");
        }
        catch (CryptographicException exception)
        {
            return ConnectionCheck.Failed($"无法加载证书 {fullPath}：{exception.Message}");
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}

/// <summary>保存宿主配置前对一组连接配置的实际检查结果。</summary>
public sealed class ConnectionCheck
{
    /// <summary>检查是否通过。</summary>
    public required bool Succeeded { get; init; }

    /// <summary>面向管理员的说明：失败时为原因，成功时为补充信息（如连上的账号），没有时为空。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>通过且没有补充信息。</summary>
    public static ConnectionCheck Passed { get; } = new() { Succeeded = true };

    /// <summary>通过并附带补充信息。</summary>
    public static ConnectionCheck PassedWith(string message) =>
        new()
        {
            Succeeded = true,
            Message   = message
        };

    /// <summary>未通过。</summary>
    public static ConnectionCheck Failed(string message) =>
        new()
        {
            Succeeded = false,
            Message   = message
        };
}
