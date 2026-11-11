namespace Shinano.Plugin.NB2Bridge;

/// <summary>
/// NB2 运行环境在部署数据目录下的布局（<c>nb2/</c>）。目录名、插件清单与锁定文件名与 <c>Shinano.Tool nb2-setup</c> 一致。
/// </summary>
public sealed class Nb2Layout
{
    /// <summary>NB2 运行环境目录名，位于部署数据目录下。</summary>
    public const string DIRECTORY_NAME = "nb2";

    /// <summary>插件清单文件名。</summary>
    public const string PLUGINS_FILE = "plugins.json";

    /// <summary>依赖锁定文件名。</summary>
    public const string LOCK_FILE = "requirements.lock";

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

    /// <summary>依赖锁定文件，安装或卸载失败时据此还原。</summary>
    public string LockFile => Path.Combine(Root, LOCK_FILE);

    /// <summary>上传的 wheel 文件。</summary>
    public string WheelsDirectory => Path.Combine(Root, "wheels");

    /// <summary>NB2 进程输出日志。</summary>
    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>NB2 插件经 localstore 保存的数据、配置与缓存。</summary>
    public string StoreDirectory => Path.Combine(Root, "store");
}
