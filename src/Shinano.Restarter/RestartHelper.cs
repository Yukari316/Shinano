using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Shinano.Restarter;

/// <summary>
/// 辅助程序模式的重启：等旧宿主退出，启动新宿主并轮询就绪接口。带插件包变更时，新进程退出或 60 秒内未就绪
/// 即结束它并再启动一次，由宿主在启动时还原上一套插件包。辅助程序不修改任何文件，完成后退出。
/// </summary>
internal sealed class RestartHelper
{
    private static readonly TimeSpan HostExitTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ReadyTimeout    = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval    = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RequestTimeout  = TimeSpan.FromSeconds(3);

    // 宿主就绪接口返回的启动阶段中，表示启动流程已结束的值；故障模式与插件包无关，不触发回滚。
    private static readonly string[] ReadyPhases = ["Ready", "FaultMode"];

    private readonly RestarterRequest _request;
    private readonly RestartLog       _log;
    private readonly HttpClient       _http;

    public RestartHelper(RestarterRequest request, RestartLog log)
    {
        _request = request;
        _log     = log;
        _http    = CreateClient(request.CertificateThumbprint);
    }

    /// <summary>执行重启。</summary>
    /// <returns>新宿主（或回滚后的宿主）是否就绪。</returns>
    public async Task<bool> RunAsync()
    {
        await WaitForHostExitAsync();

        Process host = Start("启动新进程");
        if (await WaitReadyAsync(host)) return true;
        if (!_request.RollbackOnFailure)
        {
            _log.Error("新进程未能在 60 秒内就绪；本次重启没有插件包变更，保留该进程，请查看宿主日志");
            return false;
        }

        Stop(host);
        Process rollback = Start("启动进程以还原上一套插件包");
        if (await WaitReadyAsync(rollback))
        {
            _log.Warning("已还原上一套插件包，旧版本已就绪");
            return true;
        }

        _log.Error("还原后的进程同样未能就绪，请查看宿主日志");
        return false;
    }

    private async Task WaitForHostExitAsync()
    {
        Process host;
        try
        {
            host = Process.GetProcessById(_request.HostProcessId);
        }
        catch (ArgumentException)
        {
            _log.Info($"旧进程 {_request.HostProcessId} 已退出");
            return;
        }

        using CancellationTokenSource timeout = new(HostExitTimeout);
        try
        {
            await host.WaitForExitAsync(timeout.Token);
            _log.Info($"旧进程 {_request.HostProcessId} 已退出");
        }
        catch (OperationCanceledException)
        {
            _log.Warning($"旧进程 {_request.HostProcessId} 在 {HostExitTimeout.TotalSeconds} 秒内未退出，结束该进程");
            Stop(host);
        }
    }

    private Process Start(string purpose)
    {
        ProcessStartInfo info = new(_request.Command)
        {
            WorkingDirectory = _request.WorkingDirectory,
            UseShellExecute  = false
        };
        foreach (string argument in _request.Arguments) info.ArgumentList.Add(argument);

        Process process = Process.Start(info) ?? throw new InvalidOperationException($"无法启动 {_request.Command}");
        _log.Info($"{purpose}：{_request.Command}，PID {process.Id}");
        return process;
    }

    private async Task<bool> WaitReadyAsync(Process process)
    {
        DateTime deadline = DateTime.UtcNow + ReadyTimeout;
        string? lastPhase = null;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                _log.Error($"进程 {process.Id} 在就绪前退出，退出码 {process.ExitCode}");
                return false;
            }

            string? phase = await ReadPhaseAsync();
            if (phase is not null && phase != lastPhase)
            {
                _log.Info($"进程 {process.Id} 启动阶段：{phase}");
                lastPhase = phase;
            }

            if (phase is not null && ReadyPhases.Contains(phase)) return true;
            await Task.Delay(PollInterval);
        }

        _log.Error($"进程 {process.Id} 未能在 {ReadyTimeout.TotalSeconds} 秒内就绪");
        return false;
    }

    // 连接失败与非 2xx 响应都视为尚未就绪。
    private async Task<string?> ReadPhaseAsync()
    {
        try
        {
            using CancellationTokenSource timeout = new(RequestTimeout);
            using HttpResponseMessage response = await _http.GetAsync(_request.ReadyUrl, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;

            await using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
            return document.RootElement.TryGetProperty("phase", out JsonElement phase) ? phase.GetString() : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    private void Stop(Process process)
    {
        if (process.HasExited) return;
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            _log.Warning($"已结束进程 {process.Id}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _log.Error($"结束进程 {process.Id} 失败：{exception.Message}");
        }
    }

    // 宿主与辅助程序在同一台机器，按指纹只信任面板配置的证书，不依赖系统信任链与主机名。
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
