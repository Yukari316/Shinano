namespace Shinano.Plugin.NB2Bridge.Runtime;

/// <summary>
/// NB2 运行环境在部署数据目录下的布局（<c>nb2/</c>）。目录名、插件清单、实际基础锁定文件与插件层锁定文件名与 <c>Shinano.Tool nb2-setup</c> 一致。
/// </summary>
public sealed class Nb2Layout
{
    /// <summary>NB2 运行环境目录名，位于部署数据目录下。</summary>
    public const string DIRECTORY_NAME = "nb2";

    /// <summary>插件清单文件名。</summary>
    public const string PLUGINS_FILE = "plugins.json";

    /// <summary>实际基础锁定文件名：当前 venv 已同步的 NB2 基础环境，与 NB2Bridge 资产中的期望基础锁定文件同名。</summary>
    public const string BASE_LOCK_FILE = "base.lock";

    /// <summary>插件层锁定文件名：已验证插件层的确切版本与哈希。</summary>
    public const string PLUGINS_LOCK_FILE = "plugins.lock";

    /// <summary>管理员约束文件名。</summary>
    public const string CONSTRAINTS_FILE = "constraints.txt";

    /// <summary>NB2 入口脚本名。</summary>
    public const string BOT_SCRIPT = "bot.py";

    /// <summary>控制插件所在目录名。</summary>
    public const string CONTROL_DIRECTORY = "control";

    /// <summary>创建布局。</summary>
    /// <param name="dataDirectory">部署数据目录。</param>
    public Nb2Layout(string dataDirectory)
    {
        Root = Path.Combine(dataDirectory, DIRECTORY_NAME);
    }

    /// <summary>NB2 运行环境根目录，也是 NB2 进程的工作目录。</summary>
    public string Root { get; }

    /// <summary>venv 中的 Python 解释器。</summary>
    public string Python =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Root, ".venv", "Scripts", "python.exe")
            : Path.Combine(Root, ".venv", "bin", "python");

    /// <summary>入口脚本。</summary>
    public string BotScript => Path.Combine(Root, BOT_SCRIPT);

    /// <summary>控制插件目录（加入 Python 模块搜索路径）。</summary>
    public string ControlDirectory => Path.Combine(Root, CONTROL_DIRECTORY);

    /// <summary>插件配置文件，NoneBot 启动时读取。</summary>
    public string EnvFile => Path.Combine(Root, ".env");

    /// <summary>插件清单。</summary>
    public string PluginsFile => Path.Combine(Root, PLUGINS_FILE);

    /// <summary>实际基础锁定文件；缺失表示环境不是按锁定文件建立的。</summary>
    public string BaseLockFile => Path.Combine(Root, BASE_LOCK_FILE);

    /// <summary>插件层锁定文件；与实际基础锁定文件一起用于还原与基础层不变时的重建。</summary>
    public string PluginsLockFile => Path.Combine(Root, PLUGINS_LOCK_FILE);

    /// <summary>管理员约束文件，排除已知有问题的版本；所有插件安装与重建都附加。</summary>
    public string ConstraintsFile => Path.Combine(Root, CONSTRAINTS_FILE);

    /// <summary>上传的 wheel 文件。</summary>
    public string WheelsDirectory => Path.Combine(Root, "wheels");

    /// <summary>NB2 进程输出日志。</summary>
    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>NB2 插件经 localstore 保存的数据、配置与缓存。</summary>
    public string StoreDirectory => Path.Combine(Root, "store");
}
