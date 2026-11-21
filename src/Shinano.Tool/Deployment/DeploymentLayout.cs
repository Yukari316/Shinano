namespace Shinano.Tool.Deployment;

/// <summary>
/// 部署目录结构：<c>host/</c>（宿主与工具）、<c>monitor/</c>、<c>bundled-plugins/</c>、<c>scripts/</c> 与部署数据目录 <c>data/</c>。
/// 数据目录固定为部署根下的 <c>data/</c>，由工具通过 <see cref="DATA_DIRECTORY_VARIABLE" /> 交给宿主与监控。
/// </summary>
public sealed class DeploymentLayout
{
    /// <summary>宿主与监控读取部署数据目录的环境变量名。</summary>
    public const string DATA_DIRECTORY_VARIABLE = "SHINANO_DATA_DIR";

    /// <summary>部署配置文件名，与宿主一致。</summary>
    public const string CONFIG_FILE_NAME = "shinano.json";

    /// <summary>升级时替换的程序目录；部署数据目录不在其中。</summary>
    public static readonly IReadOnlyList<string> ProgramDirectoryNames = [HOST_DIRECTORY, MONITOR_DIRECTORY, BUNDLED_PLUGINS_DIRECTORY, SCRIPTS_DIRECTORY];

    private const string HOST_DIRECTORY            = "host";
    private const string MONITOR_DIRECTORY         = "monitor";
    private const string BUNDLED_PLUGINS_DIRECTORY = "bundled-plugins";
    private const string SCRIPTS_DIRECTORY         = "scripts";
    private const string DATA_DIRECTORY            = "data";
    private const string PREVIOUS_DIRECTORY        = ".previous";
    private const string STATE_FILE_NAME           = "deployment.json";
    private const string TOOL_LOG_FILE_NAME        = "tool.log";
    private const string HOST_NAME                 = "Shinano.Host";
    private const string MONITOR_NAME              = "Shinano.Monitor";
    private const string TOOL_NAME                 = "Shinano.Tool";
    private const string WINDOWS_EXECUTABLE_SUFFIX = ".exe";

    private DeploymentLayout(string root)
    {
        Root = Path.GetFullPath(root);
    }

    /// <summary>部署根目录。</summary>
    public string Root { get; }

    /// <summary>宿主与工具所在目录。</summary>
    public string HostDirectory => Path.Combine(Root, HOST_DIRECTORY);

    /// <summary>监控所在目录。</summary>
    public string MonitorDirectory => Path.Combine(Root, MONITOR_DIRECTORY);

    /// <summary>随包插件（插件包 zip）所在目录。</summary>
    public string BundledPluginsDirectory => Path.Combine(Root, BUNDLED_PLUGINS_DIRECTORY);

    /// <summary>部署数据目录。</summary>
    public string DataDirectory => Path.Combine(Root, DATA_DIRECTORY);

    /// <summary>上一版程序目录的备份，只保留最近一次升级前的版本。</summary>
    public string PreviousDirectory => Path.Combine(Root, PREVIOUS_DIRECTORY);

    /// <summary>部署配置文件。</summary>
    public string ConfigPath => Path.Combine(DataDirectory, CONFIG_FILE_NAME);

    /// <summary>工具自己的部署状态文件，宿主不读取。</summary>
    public string StatePath => Path.Combine(DataDirectory, STATE_FILE_NAME);

    /// <summary>工具的操作记录。</summary>
    public string ToolLogPath => Path.Combine(DataDirectory, TOOL_LOG_FILE_NAME);

    /// <summary>宿主可执行文件。</summary>
    public string HostExecutable => Path.Combine(HostDirectory, Executable(HOST_NAME));

    /// <summary>监控可执行文件。</summary>
    public string MonitorExecutable => Path.Combine(MonitorDirectory, Executable(MONITOR_NAME));

    /// <summary>工具可执行文件。</summary>
    public string ToolExecutable => Path.Combine(HostDirectory, Executable(TOOL_NAME));

    /// <summary>宿主程序集，用于读取版本号。</summary>
    public string HostAssembly => Path.Combine(HostDirectory, HOST_NAME + ".dll");

    /// <summary>当前运行的工具所在的部署：工具位于 <c>&lt;部署根&gt;/host/</c>。</summary>
    public static DeploymentLayout Current() =>
        new(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)) ?? AppContext.BaseDirectory);

    /// <summary>指定部署根目录的部署。</summary>
    public static DeploymentLayout At(string root) => new(root);

    /// <summary>两个路径是否指向同一位置：先转为完整路径并去掉末尾分隔符，Windows 上不区分大小写。</summary>
    public static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Executable(string name) => OperatingSystem.IsWindows() ? name + WINDOWS_EXECUTABLE_SUFFIX : name;
}
