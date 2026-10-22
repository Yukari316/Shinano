using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Shinano.Host.Bot;
using Shinano.Host.Configuration;
using Shinano.Host.Notifications;
using Shinano.Host.Packages;
using Shinano.Host.Plugins;
using Shinano.Host.Web;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Logging;
using Shinano.Restarter;

namespace Shinano.Host.Restart;

/// <summary>
/// 面板发起的重启与插件包变更的应用、验证和回滚。
/// <list type="bullet">
/// <item>重启时把待处理变更写成计划，按重启方式启动辅助程序或以约定退出码退出。</item>
/// <item>新进程在加载插件前应用计划（备份后替换目录），加载后验证变更的插件包；验证失败时以约定退出码退出，
/// 下一次启动还原备份。验证前的任何一次启动都会还原，进程崩溃或被辅助程序结束同样如此。</item>
/// <item>基础设施处于故障模式时插件不加载，变更记为未验证，不回滚。</item>
/// </list>
/// </summary>
public sealed class RestartCoordinator : IDisposable
{
    private const string DIRECTORY_NAME      = "restart";
    private const string PLAN_FILE           = "plan.json";
    private const string RESULT_FILE         = "result.json";
    private const string HELPER_REQUEST_FILE = "helper.json";
    private const string APPSETTINGS         = "appsettings.json";
    private const string DOTNET_HOST         = "dotnet";
    private const int    MAX_REASON_LENGTH   = 300;

    private static readonly TimeSpan StopDelay = TimeSpan.FromMilliseconds(500);

    private readonly DeploymentPaths              _paths;
    private readonly RestartOptions               _options;
    private readonly string                       _pluginRoot;
    private readonly PluginPackageStaging         _staging;
    private readonly IHostApplicationLifetime     _lifetime;
    private readonly IHostLifetime?               _hostLifetime;
    private readonly IHostEnvironment             _environment;
    private readonly LogFileLayout                _logLayout;
    private readonly SuperUserNotifier            _notifier;
    private readonly RuntimeHealth                _health;
    private readonly TimeProvider                 _time;
    private readonly ILogger<RestartCoordinator>  _logger;
    private readonly Lock                         _lock = new();
    private          RestartResult?               _lastResult;
    private          NotificationCard?            _rollbackNotice;
    private          IReadOnlyList<string>        _loadFailures = [];
    private volatile StartupPhase                 _phase        = StartupPhase.Starting;
    private          bool                         _restarting;

    /// <summary>创建协调器。</summary>
    public RestartCoordinator(
        DeploymentPaths             paths,
        RestartOptions              options,
        PluginRuntimeSettings       pluginSettings,
        PluginPackageStaging        staging,
        IHostApplicationLifetime    lifetime,
        IHostLifetime?              hostLifetime,
        IHostEnvironment            environment,
        LogFileLayout               logLayout,
        SuperUserNotifier           notifier,
        RuntimeHealth               health,
        TimeProvider                time,
        ILogger<RestartCoordinator> logger)
    {
        _paths        = paths;
        _options      = options;
        _pluginRoot   = pluginSettings.PluginRoot;
        _staging      = staging;
        _lifetime     = lifetime;
        _hostLifetime = hostLifetime;
        _environment  = environment;
        _logLayout    = logLayout;
        _notifier     = notifier;
        _health       = health;
        _time         = time;
        _logger       = logger;
        Directory.CreateDirectory(StateDirectory);
        _health.Changed += OnHealthChanged;
    }

    /// <summary>本进程的实例 ID；面板据此判断重启后连上的是新进程。</summary>
    public Guid InstanceId { get; } = Guid.NewGuid();

    /// <summary>启动阶段。</summary>
    public StartupPhase Phase => _phase;

    /// <summary>重启方式。</summary>
    public RestartMode Mode => _options.Mode;

    /// <summary>是否已发起重启。</summary>
    public bool Restarting
    {
        get
        {
            lock (_lock)
                return _restarting;
        }
    }

    /// <summary>加载失败的插件 ID；插件加载前为空。</summary>
    public IReadOnlyList<string> LoadFailures => _loadFailures;

    /// <summary>最近一次带插件包变更的重启结果；管理员确认后为 null。</summary>
    public RestartResult? LastResult
    {
        get
        {
            lock (_lock)
                return _lastResult;
        }
    }

    private string StateDirectory => Path.Combine(_paths.DataDirectory, DIRECTORY_NAME);

    private string PlanPath => Path.Combine(StateDirectory, PLAN_FILE);

    private string ResultPath => Path.Combine(StateDirectory, RESULT_FILE);

    /// <summary>
    /// 在加载插件前调用一次：列出待处理变更；应用待应用的计划，或还原上一次未通过验证的计划。
    /// </summary>
    public void PrepareStartup()
    {
        lock (_lock)
            _lastResult = StateFile.Read<RestartResult>(ResultPath);

        if (StateFile.Read<RestartPlan>(PlanPath) is { } plan)
        {
            if (plan.State == RestartPlanState.Pending)
                Apply(plan);
            else
                RollBack(
                    plan,
                    plan.State == RestartPlanState.LoadFailed && plan.FailureReason is { } failure
                        ? failure
                        : "新版本未能完成启动（进程退出或未在 60 秒内就绪）");
        }

        foreach (PendingPluginChange item in _staging.Pending())
            _logger.LogInformation(
                "待处理的插件包变更（面板重启后生效）：{Kind} {PluginId} {FromVersion} → {ToVersion}",
                item.Change.Kind,
                item.Change.PluginId,
                item.Change.FromVersion,
                item.Change.ToVersion);
    }

    /// <summary>基础设施进入故障模式时调用：插件不会加载，已应用的变更记为未验证。</summary>
    public void OnFaultMode()
    {
        _phase = StartupPhase.FaultMode;
        if (StateFile.Read<RestartPlan>(PlanPath) is not { State: RestartPlanState.Applied } plan) return;

        _logger.LogWarning("基础设施处于故障模式，已应用的插件包未经加载验证");
        Finish(plan, RestartOutcome.Unverified, "基础设施处于故障模式，插件包已应用但未经加载验证");
    }

    /// <summary>
    /// 插件加载完成后调用，验证本次变更的插件包。
    /// </summary>
    /// <param name="loadErrors">已发现插件的加载结果：插件 ID 到失败原因，成功为 null。</param>
    /// <returns>是否继续启动；为 false 时进程已开始以约定退出码退出，下一次启动还原。</returns>
    public bool OnPluginsLoaded(IReadOnlyDictionary<string, string?> loadErrors)
    {
        _loadFailures = [.. loadErrors.Where(pair => pair.Value is not null).Select(pair => pair.Key).Order(StringComparer.Ordinal)];
        if (StateFile.Read<RestartPlan>(PlanPath) is not { State: RestartPlanState.Applied } plan)
        {
            _phase = StartupPhase.Ready;
            return true;
        }

        List<string> failures = [];
        foreach (PluginChange change in plan.Operations.Select(operation => operation.Change)
                                             .Where(change => change.Kind != PluginChangeKind.Remove))
        {
            if (!loadErrors.TryGetValue(change.PluginId, out string? error)) failures.Add($"插件 {change.Name} 未被发现");
            else if (error is not null) failures.Add($"插件 {change.Name} 加载失败：{error}");
        }

        if (failures.Count == 0)
        {
            _logger.LogInformation("插件包变更已通过加载验证");
            Finish(plan, RestartOutcome.Succeeded, null);
            _phase = StartupPhase.Ready;
            return true;
        }

        plan.State         = RestartPlanState.LoadFailed;
        plan.FailureReason = Truncate(HidePaths(string.Join("；", failures)));
        StateFile.Write(PlanPath, plan);
        _logger.LogError("插件包变更未通过加载验证，退出后由下一次启动还原：{Reason}", string.Join("；", failures));
        SetRestartExitCode();
        _lifetime.StopApplication();
        return false;
    }

    /// <summary>
    /// 发起重启：应用全部待处理变更。辅助程序模式下启动辅助程序后退出；服务管理器模式下以约定退出码退出。
    /// </summary>
    /// <returns>无法发起时的原因；为 null 时进程将在响应发出后退出。</returns>
    public string? RequestRestart()
    {
        lock (_lock)
        {
            if (_restarting) return "正在重启";
            _restarting = true;
        }

        IReadOnlyList<PendingPluginChange> pending = _staging.Freeze();
        try
        {
            List<PluginPackageOperation> operations = [.. pending.Select(item => ToOperation(item.Change)).OfType<PluginPackageOperation>()];
            if (operations.Count > 0)
                StateFile.Write(
                    PlanPath,
                    new RestartPlan
                    {
                        Mode       = _options.Mode,
                        CreatedAt  = _time.GetUtcNow(),
                        Operations = operations
                    });

            if (_options.Mode == RestartMode.Helper) StartHelper(operations.Count > 0);
            else SetRestartExitCode();

            _logger.LogWarning(
                "面板发起重启（{Mode}），应用 {Count} 项插件包变更",
                _options.Mode,
                operations.Count);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException
                                              or System.ComponentModel.Win32Exception)
        {
            StateFile.Delete(PlanPath);
            _staging.Unfreeze();
            lock (_lock)
                _restarting = false;
            _logger.LogError(exception, "无法发起重启");
            return $"无法发起重启：{exception.Message}";
        }

        _ = StopSoonAsync();
        return null;
    }

    /// <summary>确认最近一次重启结果，从面板移除。</summary>
    public void DismissResult()
    {
        lock (_lock)
            _lastResult = null;
        StateFile.Delete(ResultPath);
    }

    /// <inheritdoc />
    public void Dispose() => _health.Changed -= OnHealthChanged;

    private void Apply(RestartPlan plan)
    {
        try
        {
            PluginPackageApplier.Apply(plan, _pluginRoot, value => StateFile.Write(PlanPath, value));
            _staging.Consume([.. plan.Operations.Select(operation => operation.Change.PluginId)]);
            foreach (PluginChange change in plan.Operations.Select(operation => operation.Change))
                _logger.LogInformation(
                    "已应用插件包变更：{Kind} {PluginId} {FromVersion} → {ToVersion}",
                    change.Kind,
                    change.PluginId,
                    change.FromVersion,
                    change.ToVersion);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 应用失败时 Apply 已还原；暂存保留，管理员可以处理后再次重启。
            _logger.LogError(exception, "应用插件包变更失败，已还原");
            StateFile.Delete(PlanPath);
            Record(plan, RestartOutcome.RolledBack, Truncate(HidePaths($"应用插件包失败：{exception.Message}")));
        }
    }

    private void RollBack(RestartPlan plan, string reason)
    {
        _logger.LogError("上一次插件包变更未通过验证，还原上一套插件包：{Reason}", reason);
        try
        {
            PluginPackageApplier.Restore(plan, _pluginRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 保留计划，下一次启动继续尝试还原；本次按插件目录现状加载。
            _logger.LogCritical(exception, "还原插件包失败，请检查插件目录 {Directory}", _pluginRoot);
            return;
        }

        StateFile.Delete(PlanPath);
        Record(plan, RestartOutcome.RolledBack, reason);
    }

    private void Finish(RestartPlan plan, RestartOutcome outcome, string? reason)
    {
        if (!_options.KeepBackup) PluginPackageApplier.DeleteDirectory(PluginPackageApplier.BackupRoot(_pluginRoot));
        StateFile.Delete(PlanPath);
        Record(plan, outcome, reason);
    }

    private void Record(RestartPlan plan, RestartOutcome outcome, string? reason)
    {
        RestartResult result = new()
        {
            Outcome    = outcome,
            Mode       = plan.Mode,
            Reason     = reason,
            FinishedAt = _time.GetUtcNow(),
            Changes    = [.. plan.Operations.Select(operation => operation.Change)]
        };
        StateFile.Write(ResultPath, result);
        lock (_lock)
        {
            _lastResult = result;
            if (outcome == RestartOutcome.RolledBack) _rollbackNotice = RollbackCard(result);
        }
    }

    // 回滚发生在机器人连接之前，连接成功后再通知一次。
    private void OnHealthChanged(HealthChange change)
    {
        if (change.Current is not { Component: HealthComponent.Bot, Level: HealthLevel.Normal }) return;

        NotificationCard? notice;
        lock (_lock)
        {
            notice          = _rollbackNotice;
            _rollbackNotice = null;
        }

        if (notice is not null) _notifier.Notify(notice);
    }

    private static NotificationCard RollbackCard(RestartResult result) =>
        new()
        {
            Level = NotificationLevel.Warning,
            Title = "插件更新已回滚",
            Items =
            [
                new NotificationItem { Label = "重启方式", Value = result.Mode == RestartMode.Helper ? "辅助程序" : "服务管理器" },
                new NotificationItem { Label = "原因", Value = result.Reason ?? "未知" },
                new NotificationItem { Label = "已还原", Value = string.Join('\n', result.Changes.Select(Describe)) }
            ]
        };

    private static string Describe(PluginChange change) =>
        change.Kind switch
        {
            PluginChangeKind.Install => $"安装 {change.Name} {change.ToVersion} → 已移除",
            PluginChangeKind.Upgrade => $"升级 {change.Name} {change.ToVersion} → 已还原为 {change.FromVersion}",
            _                        => $"删除 {change.Name} {change.FromVersion} → 已恢复"
        };

    // 以插件目录现状确定目标：已安装则替换其目录（暂存后被外部安装的视为升级），未安装则安装到 <插件目录>/<id>。
    private PluginPackageOperation? ToOperation(PluginChange change)
    {
        PluginPackageDirectory? installed = _staging.FindInstalled(change.PluginId);
        if (change.Kind == PluginChangeKind.Remove)
            return installed is null
                ? null
                : new PluginPackageOperation
                {
                    Change          = change,
                    TargetDirectory = installed.Directory
                };

        string staged = _staging.PackageDirectory(change.PluginId);
        if (installed is not null)
            return new PluginPackageOperation
            {
                Change = new PluginChange
                {
                    Kind        = PluginChangeKind.Upgrade,
                    PluginId    = change.PluginId,
                    Name        = change.Name,
                    FromVersion = installed.Manifest?.Version,
                    ToVersion   = change.ToVersion
                },
                StagedDirectory = staged,
                TargetDirectory = installed.Directory
            };

        string target = Path.Combine(_pluginRoot, change.PluginId);
        if (Directory.Exists(target))
            throw new InvalidOperationException($"插件目录下已存在 {change.PluginId}，但不是该插件的插件包");
        return new PluginPackageOperation
        {
            Change          = change,
            StagedDirectory = staged,
            TargetDirectory = target
        };
    }

    private void StartHelper(bool rollbackOnFailure)
    {
        string restarter = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Shinano.Restarter.exe" : "Shinano.Restarter");
        if (!File.Exists(restarter)) throw new InvalidOperationException($"缺少重启辅助程序 {restarter}");

        // SAFE: 运行中的进程总有可执行文件路径。
        string command = Environment.ProcessPath!;
        string[] arguments = Environment.GetCommandLineArgs()[1..];
        if (Path.GetFileNameWithoutExtension(command).Equals(DOTNET_HOST, StringComparison.OrdinalIgnoreCase)
            && Assembly.GetEntryAssembly()?.Location is { Length: > 0 } entry)
            arguments = [entry, .. arguments];

        // 新进程按已保存的配置监听，端口或证书可能已在面板中修改，需要按当前配置文件重新解析。
        WebEndpoint endpoint = WebEndpointResolver.Resolve(ReadWebOptions(), _paths.DataDirectory);
        string host = endpoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{endpoint.Address}]"
            : endpoint.Address.ToString();
        string? thumbprint = endpoint.Certificate?.GetCertHashString(HashAlgorithmName.SHA256);
        endpoint.Certificate?.Dispose();

        string requestPath = Path.Combine(StateDirectory, HELPER_REQUEST_FILE);
        StateFile.Write(
            requestPath,
            new RestarterRequest
            {
                HostProcessId         = Environment.ProcessId,
                Command               = command,
                Arguments             = arguments,
                WorkingDirectory      = Environment.CurrentDirectory,
                ReadyUrl              = $"{(thumbprint is null ? "http" : "https")}://{host}:{endpoint.Port}{ReadinessEndpoint.PATH}",
                CertificateThumbprint = thumbprint,
                RollbackOnFailure     = rollbackOnFailure,
                LogDirectory          = _logLayout.Directory
            });

        ProcessStartInfo info = new(restarter)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute  = false
        };
        info.ArgumentList.Add(RestarterRequest.REQUEST_ARGUMENT);
        info.ArgumentList.Add(requestPath);
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("无法启动重启辅助程序");
        _logger.LogInformation("已启动重启辅助程序，PID {ProcessId}", process.Id);
    }

    private WebOptions ReadWebOptions()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
                                           .AddJsonFile(Path.Combine(_paths.BaseDirectory, APPSETTINGS), optional: true)
                                           .AddJsonFile(Path.Combine(_paths.BaseDirectory, $"appsettings.{_environment.EnvironmentName}.json"), optional: true)
                                           .AddEnvironmentVariables()
                                           .AddCommandLine(Environment.GetCommandLineArgs()[1..])
                                           .AddJsonFile(_paths.LocalConfigPath, optional: true)
                                           .Build();
        return configuration.GetSection(WebOptions.SECTION_NAME).Get<WebOptions>() ?? new WebOptions();
    }

    // Windows 服务停止时向服务管理器报告的是 ServiceBase.ExitCode 而不是进程退出码，两者都设置，
    // 服务管理器才把这次退出视为失败并按恢复设置重启；systemd 只看进程退出码。
    private void SetRestartExitCode()
    {
        Environment.ExitCode = RestartOptions.SERVICE_RESTART_EXIT_CODE;
        if (OperatingSystem.IsWindows() && _hostLifetime is System.ServiceProcess.ServiceBase service)
            service.ExitCode = RestartOptions.SERVICE_RESTART_EXIT_CODE;
    }

    private async Task StopSoonAsync()
    {
        // 稍等让重启请求的响应先发出。
        await Task.Delay(StopDelay);
        _lifetime.StopApplication();
    }

    // 原因会显示在面板与通知卡片中，不暴露部署机上的目录结构；完整信息以日志为准。
    private string HidePaths(string text) =>
        text.Replace(_pluginRoot, "<插件目录>", StringComparison.OrdinalIgnoreCase)
            .Replace(_paths.DataDirectory, "<数据目录>", StringComparison.OrdinalIgnoreCase)
            .Replace(_paths.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "<程序目录>", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string text) => text.Length <= MAX_REASON_LENGTH ? text : text[..MAX_REASON_LENGTH] + "…";
}
