using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Framework.Plugins.Models;
using Shinano.Framework.Rendering.Models;
using Shinano.Plugin.NB2Bridge.Models;

namespace Shinano.Plugin.NB2Bridge;

/// <summary>
/// NB2 进程的守护：随插件启用启动、停用停止；启动前覆盖 <c>bot.py</c> 与控制插件，生成本次的 token 与端口，启动 Milky 实现端；
/// NB2 连上后先推送禁用集合再开始交付事件。意外退出时自动重启，1 分钟内超过上限后停止并通知 Super User 一次。
/// 停止先经控制 API 请求正常关停，超时强制结束。启动、停止与包变更串行执行。
/// </summary>
public sealed class Nb2Runner : IDisposable
{
    private const string BOT_ASSET           = "nb2/bot.py";
    private const string CONTROL_ASSET       = "nb2/control/shinano_nb2_control/__init__.py";
    private const string CONTROL_PACKAGE     = "shinano_nb2_control";
    private const string CONTROL_INIT        = "__init__.py";
    private const int    TOKEN_BYTES         = 32;
    private const long   BYTES_PER_MIB       = 1024 * 1024;
    private const int    PREPARE_ATTEMPTS    = 20;
    private const string FFMPEG_VERSION_WORD = "version";

    private static readonly TimeSpan CrashWindow   = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PrepareDelay  = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(3);

    private readonly IPluginInitializationContext _context;
    private readonly Nb2Layout                    _layout;
    private readonly Nb2Catalog                   _catalog;
    private readonly MilkyActions                 _actions;
    private readonly Nb2LogSink                   _log;
    private readonly TimeProvider                 _time;
    private readonly ILogger<Nb2Runner>           _logger;
    private readonly SemaphoreSlim                _gate    = new(1, 1);
    private readonly Queue<DateTimeOffset>        _crashes = new();

    private volatile Process?                       _process;
    private volatile MilkyServer?                   _server;
    private volatile Nb2ControlClient?              _control;
    private volatile bool                           _ready;
    private volatile Nb2ProcessState                _state = Nb2ProcessState.Stopped;
    private          IReadOnlyList<Nb2EnvironmentVariable> _environment = [];
    private          DateTimeOffset?                _startedAt;
    private          int?                           _lastExitCode;
    private          string?                        _lastError;
    private          string?                        _ffmpegDirectory;
    private          string?                        _ffmpegError;
    private          string?                        _ffmpegVersion;

    /// <summary>创建守护。</summary>
    public Nb2Runner(
        IPluginInitializationContext context,
        Nb2Layout                    layout,
        Nb2Catalog                   catalog,
        MilkyActions                 actions,
        TimeProvider                 time,
        ILogger<Nb2Runner>           logger)
    {
        _context = context;
        _layout  = layout;
        _catalog = catalog;
        _actions = actions;
        _log     = new Nb2LogSink(layout.LogsDirectory);
        _time    = time;
        _logger  = logger;
    }

    /// <summary>是否正在交付事件：已连上且已推送禁用集合。</summary>
    public bool IsReady => _ready;

    /// <summary>当前的 Milky 实现端；未运行时为 null。</summary>
    public MilkyServer? Server => _server;

    /// <summary>当前的控制 API 客户端；未运行时为 null。</summary>
    public Nb2ControlClient? Control => _control;

    /// <summary>NB2 进程输出的最近若干行。</summary>
    public IReadOnlyList<string> RecentLog(int count) => _log.Recent(count);

    /// <summary>状态快照。</summary>
    public Nb2RunnerStatus Status()
    {
        Process? process = _process;
        return new Nb2RunnerStatus
        {
            State           = _state,
            Pid             = process?.Id,
            StartedAt       = _startedAt,
            Connected       = _server?.IsConnected ?? false,
            RecentCrashes   = RecentCrashes(),
            LastExitCode    = _lastExitCode,
            LastError       = _lastError,
            FfmpegDirectory = _ffmpegDirectory,
            FfmpegError     = _ffmpegError,
            FfmpegVersion   = _ffmpegVersion,
            Environment     = _environment
        };
    }

    /// <summary>启动 NB2；已在运行时不做任何事。手动启动清除自动重启计数。</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            lock (_crashes) _crashes.Clear();
            await StartCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>停止 NB2。</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>重启 NB2（使导入时读取的配置生效）。</summary>
    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            lock (_crashes) _crashes.Clear();
            await StartCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>停止 NB2 后执行包变更，完成后（无论成败）恢复原来的运行状态。</summary>
    public async Task<T> ExclusiveAsync<T>(Func<CancellationToken, Task<T>> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            bool running = _process is not null;
            if (running) await StopCoreAsync();
            try
            {
                return await change(cancellationToken);
            }
            finally
            {
                if (running) await StartCoreAsync(CancellationToken.None);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>插件启停回调：启用时在后台启动 NB2，停用时在后台停止。</summary>
    public void OnEnabledChanged(bool enabled) =>
        _ = Task.Run(async () =>
        {
            try
            {
                if (enabled) await StartAsync(CancellationToken.None);
                else await StopAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "随插件{Action} NB2 失败", enabled ? "启用启动" : "停用停止");
            }
        });

    /// <summary>就绪时把当前的禁用集合推送给 NB2；未就绪时连上后会推送。</summary>
    /// <exception cref="HttpRequestException">NB2 拒绝或不可达。</exception>
    public async Task PushDisabledAsync(CancellationToken cancellationToken)
    {
        if (_ready && _control is { } control) await control.SetDisabledAsync(Disabled(), cancellationToken);
    }

    /// <summary>宿主退出时停止 NB2。</summary>
    public void Dispose()
    {
        try
        {
            if (_gate.Wait(ShutdownGrace)) StopCoreAsync().GetAwaiter().GetResult();
            else _process?.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "宿主退出时停止 NB2 失败");
        }

        _log.Dispose();
    }

    private string[] Disabled() => [.. _catalog.Entries().Where(entry => !entry.Enabled).Select(entry => entry.Module)];

    private int RecentCrashes()
    {
        DateTimeOffset cutoff = _time.GetUtcNow() - CrashWindow;
        lock (_crashes)
        {
            while (_crashes.TryPeek(out DateTimeOffset oldest) && oldest < cutoff) _crashes.Dequeue();
            return _crashes.Count;
        }
    }

    // 调用方持有 _gate。
    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        if (_process is not null) return;
        if (!File.Exists(_layout.Python))
        {
            _state     = Nb2ProcessState.Missing;
            _lastError = "未找到 NB2 运行环境，请在部署机运行 scripts 下的 nb2-setup";
            _logger.LogWarning("未找到 NB2 运行环境 {Python}，不启动 NB2；请运行 scripts 下的 nb2-setup", _layout.Python);
            return;
        }

        _state = Nb2ProcessState.Starting;
        MilkyServer?      server  = null;
        Nb2ControlClient? control = null;
        try
        {
            await WriteScriptsAsync(cancellationToken);
            await ResolveFfmpegAsync(cancellationToken);

            string token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TOKEN_BYTES));
            server = await MilkyServer.StartAsync(_actions, token, _logger, cancellationToken);
            Nb2Launch launch = new()
            {
                Port            = FreePort(),
                MilkyPort       = server.Port,
                Token           = token,
                SuperUsers      = _context.Bot.SuperUsers,
                Modules         = [.. _catalog.Entries().Select(entry => entry.Module)],
                FfmpegDirectory = _ffmpegDirectory
            };
            IReadOnlyList<Nb2EnvironmentVariable> environment = Nb2Environment.Build(launch, _layout, Environment.GetEnvironmentVariable("PATH"));
            Nb2ControlClient launched = new(launch.Port, token);
            control = launched;
            server.Connected    += () => _ = PrepareAsync(launched);
            server.Disconnected += OnDisconnected;
            // 先登记本次的连接与客户端再启动进程：连接事件按引用核对是否属于当前这次启动。
            _server      = server;
            _control     = launched;
            _environment = environment;

            Nb2Config config = _context.Config<Nb2Config>().Value;
            _log.Open(config.LogFileSizeMiB * BYTES_PER_MIB, config.LogFileCount);
            Process process = Launch(environment);
            _process   = process;
            _startedAt = _time.GetUtcNow();
            _lastError = null;
            _logger.LogInformation(
                "已启动 NB2 进程 {Pid}：加载 {Count} 个插件，HTTP 端口 {Port}，Milky 端口 {MilkyPort}",
                process.Id,
                launch.Modules.Count,
                launch.Port,
                launch.MilkyPort);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _server  = null;
            _control = null;
            control?.Dispose();
            if (server is not null) await server.DisposeAsync();
            _state     = Nb2ProcessState.Stopped;
            _lastError = $"启动失败：{exception.Message}";
            _logger.LogError(exception, "启动 NB2 失败");
        }
    }

    private Process Launch(IReadOnlyList<Nb2EnvironmentVariable> environment)
    {
        ProcessStartInfo start = new(_layout.Python)
        {
            WorkingDirectory       = _layout.Root,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };
        start.ArgumentList.Add(_layout.BotScript);
        foreach (Nb2EnvironmentVariable variable in environment) start.Environment[variable.Name] = variable.Value;

        Process process = new() { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, line) => { if (line.Data is { } text) _log.Write(text); };
        process.ErrorDataReceived  += (_, line) => { if (line.Data is { } text) _log.Write(text); };
        process.Exited             += (_, _) => _ = Task.Run(() => OnExitedAsync(process));
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private async Task WriteScriptsAsync(CancellationToken cancellationToken)
    {
        string package = Path.Combine(_layout.ControlDirectory, CONTROL_PACKAGE);
        Directory.CreateDirectory(package);
        await File.WriteAllBytesAsync(_layout.BotScript, await _context.Assets.ReadAllBytesAsync(BOT_ASSET, cancellationToken), cancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(package, CONTROL_INIT),
            await _context.Assets.ReadAllBytesAsync(CONTROL_ASSET, cancellationToken),
            cancellationToken);
    }

    // ffmpeg 不可用不阻止启动：只有用到 ffmpeg 的解析会失败。
    private async Task ResolveFfmpegAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ffmpegDirectory = await _context.Tools.FfmpegDirectoryAsync(cancellationToken);
            _ffmpegVersion   = await FfmpegVersionAsync(_ffmpegDirectory, cancellationToken);
            _ffmpegError     = null;
        }
        catch (InvalidOperationException exception)
        {
            _ffmpegDirectory = null;
            _ffmpegVersion   = null;
            _ffmpegError     = exception.Message;
            _logger.LogWarning("ffmpeg 不可用，NB2 中需要 ffmpeg 的功能会失败：{Reason}", exception.Message);
        }
    }

    // 首行形如「ffmpeg version n8.1-… Copyright …」，取 version 之后的一段；读取失败只影响显示。
    private async Task<string?> FfmpegVersionAsync(string directory, CancellationToken cancellationToken)
    {
        try
        {
            ProcessStartInfo start = new(Path.Combine(directory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"), ["-version"])
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true
            };
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 ffmpeg");
            string? first = await process.StandardOutput.ReadLineAsync(cancellationToken);
            await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            string[] words = (first ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int index = Array.IndexOf(words, FFMPEG_VERSION_WORD);
            return index >= 0 && index + 1 < words.Length ? words[index + 1] : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _logger.LogWarning(exception, "读取 ffmpeg 版本失败");
            return null;
        }
    }

    private static int FreePort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // NB2 的 HTTP 服务可能晚于事件连接开始接受请求，推送失败时重试。
    private async Task PrepareAsync(Nb2ControlClient control)
    {
        for (int attempt = 0; attempt < PREPARE_ATTEMPTS; attempt++)
        {
            if (!ReferenceEquals(control, _control)) return;
            try
            {
                await control.SetDisabledAsync(Disabled(), CancellationToken.None);
                if (!ReferenceEquals(control, _control)) return;
                _ready = true;
                _state = Nb2ProcessState.Running;
                _logger.LogInformation("NB2 已就绪，开始交付事件");
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                await Task.Delay(PrepareDelay);
            }
        }

        _lastError = "NB2 已连接，但控制 API 不可用，不交付事件";
        _logger.LogError("NB2 已连接，但推送禁用集合多次失败，不交付事件");
    }

    private void OnDisconnected()
    {
        _ready = false;
        if (_state == Nb2ProcessState.Running) _state = Nb2ProcessState.Starting;
    }

    private async Task OnExitedAsync(Process process)
    {
        await _gate.WaitAsync();
        try
        {
            // 主动停止时由 StopCoreAsync 清理，这里只处理意外退出。
            if (!ReferenceEquals(process, _process)) return;

            int exitCode = process.ExitCode;
            await CleanupAsync();
            _lastExitCode = exitCode;
            _logger.LogWarning("NB2 进程意外退出，退出码 {ExitCode}", exitCode);

            int crashes;
            lock (_crashes)
            {
                _crashes.Enqueue(_time.GetUtcNow());
            }

            crashes = RecentCrashes();
            int limit = _context.Config<Nb2Config>().Value.RestartLimit;
            if (crashes <= limit)
            {
                _logger.LogInformation("自动重启 NB2（1 分钟内第 {Count} 次）", crashes);
                await StartCoreAsync(CancellationToken.None);
                return;
            }

            _state     = Nb2ProcessState.Failed;
            _lastError = $"1 分钟内意外退出 {crashes} 次，已停止自动重启";
            _logger.LogError("NB2 1 分钟内意外退出 {Count} 次，停止自动重启", crashes);
            _context.Notices.NotifySuperUsers(
                new SuperUserNotice
                {
                    Level  = CardLevel.Error,
                    Title  = "NB2 服务已停止",
                    Code   = "NB2-STOPPED",
                    Cause  = $"NB2 进程 1 分钟内意外退出 {crashes} 次，最后退出码 {exitCode}",
                    Impact = "NB2 插件停止响应，需在 NB2Bridge 页面查看日志后手动启动"
                });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "处理 NB2 进程退出失败");
        }
        finally
        {
            _gate.Release();
        }
    }

    // 调用方持有 _gate。
    private async Task StopCoreAsync()
    {
        if (_process is not { } process)
        {
            if (_state != Nb2ProcessState.Missing) _state = Nb2ProcessState.Stopped;
            return;
        }

        _state = Nb2ProcessState.Stopping;
        _ready = false;
        _logger.LogInformation("停止 NB2 进程 {Pid}", process.Id);
        try
        {
            if (_control is { } control) await control.ShutdownAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(exception, "请求 NB2 正常关停失败");
        }

        int timeoutSeconds = _context.Config<Nb2Config>().Value.StopTimeoutSeconds;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            _logger.LogInformation("NB2 进程已正常退出，退出码 {ExitCode}", process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            _logger.LogWarning("NB2 进程未在 {Seconds} 秒内退出，已强制结束", timeoutSeconds);
        }

        _lastExitCode = process.ExitCode;
        await CleanupAsync();
        _state = Nb2ProcessState.Stopped;
    }

    // 调用方持有 _gate。
    private async Task CleanupAsync()
    {
        _ready = false;
        _process?.Dispose();
        _process = null;
        _control?.Dispose();
        _control = null;
        if (_server is { } server) await server.DisposeAsync();
        _server    = null;
        _startedAt = null;
    }
}
