using System.Text;
using Shinano.Tool.Deployment;

namespace Shinano.Tool.Services;

/// <summary>
/// systemd 服务：<c>Type=notify</c>（宿主与监控接入了 systemd 通知）、失败（含非零退出）后重启，部署数据目录经 <c>Environment=</c> 交给进程。
/// unit 文件先写在部署数据目录的 <c>service/</c> 下，再由有权限的命令安装到 <c>/etc/systemd/system</c>。
/// </summary>
public sealed class SystemdServiceManager : IServiceManager
{
    private const string SYSTEMCTL         = "systemctl";
    private const string UNIT_DIRECTORY    = "/etc/systemd/system";
    private const string UNIT_SUFFIX       = ".service";
    private const string PREPARED_UNITS    = "service";
    private const string NO_LOGIN_SHELL    = "/usr/sbin/nologin";
    private const string LOAD_STATE        = "LoadState";
    private const string ACTIVE_STATE      = "ActiveState";
    private const string EXEC_START        = "ExecStart";
    private const string USER_PROPERTY     = "User";
    private const string LOADED            = "loaded";
    private const string ACTIVE            = "active";
    private const string INACTIVE          = "inactive";
    private const string FAILED            = "failed";
    private const string EXEC_PATH_PREFIX  = "path=";
    private const int    RESTART_DELAY_SEC = 5;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <inheritdoc />
    public void Prepare(ServiceSetup setup)
    {
        string directory = Path.Combine(setup.DataDirectory, PREPARED_UNITS);
        Directory.CreateDirectory(directory);
        foreach (ServiceDefinition service in setup.Services)
            File.WriteAllText(Path.Combine(directory, service.Name + UNIT_SUFFIX), Unit(service, setup));
    }

    /// <inheritdoc />
    public IReadOnlyList<SystemCommand> InstallCommands(ServiceSetup setup)
    {
        List<SystemCommand> commands = [];
        if (setup.DedicatedUser)
        {
            if (!UserExists(setup.User))
                commands.Add(Command("useradd", $"创建专用用户 {setup.User}", "--system", "--no-create-home", "--shell", NO_LOGIN_SHELL, setup.User));
            commands.Add(Command("chown", $"把部署目录改为 {setup.User} 所有", "-R", setup.User + ":", setup.Root));
        }

        string prepared = Path.Combine(setup.DataDirectory, PREPARED_UNITS);
        foreach (ServiceDefinition service in setup.Services)
            commands.Add(
                Command(
                    "install",
                    $"安装 unit 文件 {service.Name}{UNIT_SUFFIX}",
                    "-m",
                    "644",
                    Path.Combine(prepared, service.Name + UNIT_SUFFIX),
                    UnitPath(service.Name)));
        commands.Add(Command(SYSTEMCTL, "重新加载 systemd 配置", "daemon-reload"));
        commands.Add(Command(SYSTEMCTL, "设置服务开机启动", ["enable", .. setup.Services.Select(service => service.Name)]));
        return commands;
    }

    /// <inheritdoc />
    public IReadOnlyList<SystemCommand> UninstallCommands(ServiceSetup setup, bool deleteUser, string? owner)
    {
        List<SystemCommand> commands = [];
        string[] existing = [.. setup.Services.Select(service => service.Name).Where(name => Query(name).Exists)];
        if (existing.Length > 0) commands.Add(Command(SYSTEMCTL, "取消服务开机启动", ["disable", .. existing]));
        commands.AddRange(existing.Select(name => Command("rm", $"删除 unit 文件 {name}{UNIT_SUFFIX}", "-f", UnitPath(name))));
        if (existing.Length > 0) commands.Add(Command(SYSTEMCTL, "重新加载 systemd 配置", "daemon-reload"));
        if (deleteUser && UserExists(setup.User))
        {
            if (owner is not null) commands.Add(Command("chown", $"把部署目录改回 {owner} 所有", "-R", owner + ":", setup.Root));
            commands.Add(Command("userdel", $"删除专用用户 {setup.User}", setup.User));
        }

        return commands;
    }

    /// <inheritdoc />
    public IReadOnlyList<SystemCommand> StartCommands(ServiceSetup setup) =>
        [Command(SYSTEMCTL, "启动服务", ["start", .. setup.Services.Select(service => service.Name)])];

    /// <inheritdoc />
    public IReadOnlyList<SystemCommand> StopCommands(ServiceSetup setup) =>
        [Command(SYSTEMCTL, "停止服务", ["stop", .. setup.Services.Reverse().Select(service => service.Name)])];

    /// <inheritdoc />
    public ServiceStatus Query(string name)
    {
        (int exitCode, string output) = CommandRunner.Capture(
            SYSTEMCTL,
            ["show", name, $"--property={LOAD_STATE},{ACTIVE_STATE},{EXEC_START},{USER_PROPERTY}"]);
        Dictionary<string, string> properties = new(StringComparer.Ordinal);
        foreach (string line in output.Split('\n'))
        {
            int equals = line.IndexOf('=');
            if (equals > 0) properties[line[..equals]] = line[(equals + 1)..].Trim();
        }

        bool exists = exitCode == 0 && properties.GetValueOrDefault(LOAD_STATE) == LOADED;
        string? user = exists && properties.TryGetValue(USER_PROPERTY, out string? rawUser) && rawUser.Length > 0 ? rawUser : null;
        return new ServiceStatus
        {
            Exists     = exists,
            Running    = exists && properties.GetValueOrDefault(ACTIVE_STATE) == ACTIVE,
            Executable = exists ? ExecutablePath(properties.GetValueOrDefault(EXEC_START)) : null,
            User       = user
        };
    }

    /// <inheritdoc />
    public async Task<bool> WaitAsync(string name, bool running, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            (_, string state) = CommandRunner.Capture(SYSTEMCTL, ["show", name, $"--property={ACTIVE_STATE}", "--value"]);
            string value = state.Trim();
            if (running ? value == ACTIVE : value is INACTIVE or FAILED) return true;
            await Task.Delay(PollInterval);
        }

        return false;
    }

    private static string Unit(ServiceDefinition service, ServiceSetup setup)
    {
        StringBuilder unit = new();
        unit.AppendLine("[Unit]");
        unit.AppendLine($"Description={Escape(service.Description)}");
        unit.AppendLine("After=network-online.target");
        unit.AppendLine("Wants=network-online.target");
        unit.AppendLine();
        unit.AppendLine("[Service]");
        unit.AppendLine("Type=notify");
        unit.AppendLine($"User={setup.User}");
        unit.AppendLine($"WorkingDirectory={Escape(service.WorkingDirectory)}");
        unit.AppendLine($"Environment=\"{DeploymentLayout.DATA_DIRECTORY_VARIABLE}={Escape(setup.DataDirectory)}\"");
        unit.AppendLine($"ExecStart=\"{Escape(service.Executable)}\"");
        // 宿主请求重启时以非零退出码退出，on-failure 同样会拉起。
        unit.AppendLine("Restart=on-failure");
        unit.AppendLine($"RestartSec={RESTART_DELAY_SEC}");
        unit.AppendLine();
        unit.AppendLine("[Install]");
        unit.AppendLine("WantedBy=multi-user.target");
        return unit.ToString();
    }

    // unit 文件中的 % 是说明符前缀。
    private static string Escape(string value) => value.Replace("%", "%%", StringComparison.Ordinal);

    private static string UnitPath(string name) => $"{UNIT_DIRECTORY}/{name}{UNIT_SUFFIX}";

    // ExecStart 属性形如 "{ path=/opt/shinano/host/Shinano.Host ; argv[]=... }"。
    private static string? ExecutablePath(string? execStart)
    {
        if (execStart is null) return null;
        int start = execStart.IndexOf(EXEC_PATH_PREFIX, StringComparison.Ordinal);
        if (start < 0) return null;
        start += EXEC_PATH_PREFIX.Length;
        int end = execStart.IndexOf(" ;", start, StringComparison.Ordinal);
        return (end < 0 ? execStart[start..] : execStart[start..end]).Trim();
    }

    /// <summary>系统中是否已有该用户。</summary>
    public static bool UserExists(string user) => CommandRunner.Capture("id", ["-u", user]).ExitCode == 0;

    private static SystemCommand Command(string fileName, string description, params string[] arguments) =>
        new()
        {
            FileName    = fileName,
            Arguments   = arguments,
            Description = description
        };
}
