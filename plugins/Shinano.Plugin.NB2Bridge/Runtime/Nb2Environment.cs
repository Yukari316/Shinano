using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shinano.Plugin.NB2Bridge.Runtime;

/// <summary>
/// 传给 NB2 进程的全部环境变量，集中定义名字与取值；启动进程只从这里生成环境，其他代码不出现变量名。
/// 环境变量的优先级高于 <c>.env</c>，因此进程级配置不会被插件配置覆盖。变量名与 <c>bot.py</c>、控制插件一致。
/// </summary>
public static class Nb2Environment
{
    /// <summary>NoneBot 驱动：fastapi 提供控制 API，httpx 与 websockets 供 Milky 客户端模式使用。</summary>
    public const string DRIVER = "DRIVER";

    /// <summary>NoneBot 监听地址。</summary>
    public const string HOST = "HOST";

    /// <summary>NoneBot 监听端口。</summary>
    public const string PORT = "PORT";

    /// <summary>Milky 适配器客户端模式的连接列表。</summary>
    public const string MILKY_CLIENTS = "MILKY_CLIENTS";

    /// <summary>NoneBot 的 Super User。</summary>
    public const string SUPERUSERS = "SUPERUSERS";

    /// <summary>控制 API 的 token。</summary>
    public const string TOKEN = "SHINANO_NB2_TOKEN";

    /// <summary>要加载的插件模块（JSON 数组）。</summary>
    public const string PLUGINS = "SHINANO_NB2_PLUGINS";

    /// <summary>localstore 的数据目录。</summary>
    public const string LOCALSTORE_DATA = "LOCALSTORE_DATA_DIR";

    /// <summary>localstore 的配置目录。</summary>
    public const string LOCALSTORE_CONFIG = "LOCALSTORE_CONFIG_DIR";

    /// <summary>localstore 的缓存目录。</summary>
    public const string LOCALSTORE_CACHE = "LOCALSTORE_CACHE_DIR";

    /// <summary>Python 以 UTF-8 读写文件与标准输出，避免 Windows 代码页导致日志乱码。</summary>
    public const string PYTHON_UTF8 = "PYTHONUTF8";

    /// <summary>可执行文件搜索路径；ffmpeg 目录前置，parser 按命令名调用 ffmpeg。</summary>
    public const string PATH = "PATH";

    /// <summary>为 <c>1</c> 时 <c>bot.py</c> 只试加载全部插件后退出，见 <see cref="BuildProbe" />。</summary>
    public const string PROBE = "SHINANO_NB2_PROBE";

    private const string LOOPBACK   = "127.0.0.1";
    private const string DRIVERS    = "~fastapi+~httpx+~websockets";
    private const string CONFIGURED = "（已设置）";

    /// <summary>生成本次启动的环境变量。</summary>
    /// <param name="launch">启动参数。</param>
    /// <param name="layout">NB2 运行环境布局。</param>
    /// <param name="inheritedPath">宿主进程的 PATH。</param>
    public static IReadOnlyList<Nb2EnvironmentVariable> Build(Nb2Launch launch, Nb2Layout layout, string? inheritedPath)
    {
        JsonArray clients =
        [
            new JsonObject
            {
                ["host"]         = LOOPBACK,
                ["port"]         = launch.MilkyPort,
                ["access_token"] = launch.Token
            }
        ];
        JsonArray displayedClients =
        [
            new JsonObject
            {
                ["host"]         = LOOPBACK,
                ["port"]         = launch.MilkyPort,
                ["access_token"] = CONFIGURED
            }
        ];
        string port = launch.Port.ToString(CultureInfo.InvariantCulture);
        string superUsers = JsonSerializer.Serialize(launch.SuperUsers.Select(user => user.ToString(CultureInfo.InvariantCulture)));
        string plugins = JsonSerializer.Serialize(launch.Modules);
        string store = layout.StoreDirectory;

        List<Nb2EnvironmentVariable> variables =
        [
            Plain(DRIVER, DRIVERS),
            Plain(HOST, LOOPBACK),
            Plain(PORT, port),
            new()
            {
                Name    = MILKY_CLIENTS,
                Value   = clients.ToJsonString(),
                Display = displayedClients.ToJsonString()
            },
            Plain(SUPERUSERS, superUsers),
            new()
            {
                Name    = TOKEN,
                Value   = launch.Token,
                Display = CONFIGURED
            },
            Plain(PLUGINS, plugins),
            Plain(LOCALSTORE_DATA, Path.Combine(store, "data")),
            Plain(LOCALSTORE_CONFIG, Path.Combine(store, "config")),
            Plain(LOCALSTORE_CACHE, Path.Combine(store, "cache")),
            Plain(PYTHON_UTF8, "1")
        ];
        if (launch.FfmpegDirectory is { } ffmpeg)
        {
            variables.Add(
                new Nb2EnvironmentVariable
                {
                    Name    = PATH,
                    Value   = string.IsNullOrEmpty(inheritedPath) ? ffmpeg : ffmpeg + Path.PathSeparator + inheritedPath,
                    Display = $"{ffmpeg}（前置，其余沿用宿主）"
                });
        }

        return variables;
    }

    /// <summary>
    /// 试加载（<c>bot.py</c> 的 <see cref="PROBE" /> 模式）的环境：与正式运行相同的驱动与插件列表，localstore 指向
    /// <paramref name="storeDirectory" />（调用方用完删除），不需要监听端口与 Milky 连接。
    /// </summary>
    public static IReadOnlyList<Nb2EnvironmentVariable> BuildProbe(IReadOnlyList<string> modules, string storeDirectory) =>
    [
        Plain(DRIVER, DRIVERS),
        Plain(PLUGINS, JsonSerializer.Serialize(modules)),
        Plain(LOCALSTORE_DATA, Path.Combine(storeDirectory, "data")),
        Plain(LOCALSTORE_CONFIG, Path.Combine(storeDirectory, "config")),
        Plain(LOCALSTORE_CACHE, Path.Combine(storeDirectory, "cache")),
        Plain(PYTHON_UTF8, "1"),
        Plain(PROBE, "1")
    ];

    private static Nb2EnvironmentVariable Plain(string name, string value) =>
        new()
        {
            Name    = name,
            Value   = value,
            Display = value
        };
}
