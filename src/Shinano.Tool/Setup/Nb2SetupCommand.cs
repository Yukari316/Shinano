using System.Text.Json;
using Shinano.Tool.Cli;
using Shinano.Tool.Deployment;
using Shinano.Tool.Services;

namespace Shinano.Tool.Setup;

/// <summary>
/// <c>nb2-setup</c>：用 uv 在部署数据目录的 <c>nb2/</c> 下创建 NB2 运行环境（Python 3.12 venv），安装固定版本的 NoneBot2、
/// Milky 适配器与 nonebot-plugin-parser，写入初始插件配置 <c>.env</c>、插件清单与依赖锁定文件。可重复运行：venv 已存在时只补装依赖，
/// 已有的 <c>.env</c> 与插件清单不覆盖。<c>bot.py</c> 与控制插件由 NB2Bridge 每次启动 NB2 前写入，不在这里放置。
/// </summary>
public static class Nb2SetupCommand
{
    /// <summary>NB2 运行环境所在目录名，位于部署数据目录下；与 NB2Bridge 一致。</summary>
    public const string DIRECTORY_NAME = "nb2";

    /// <summary>插件清单文件名；与 NB2Bridge 一致。</summary>
    public const string PLUGINS_FILE = "plugins.json";

    /// <summary>依赖锁定文件名；与 NB2Bridge 一致。</summary>
    public const string LOCK_FILE = "requirements.lock";

    private const string DATA_DIR_OPTION = "data-dir";
    private const string VENV_DIRECTORY  = ".venv";
    private const string ENV_FILE        = ".env";
    private const string UV              = "uv";
    private const string PYTHON_VERSION  = "3.12";
    private const string PARSER_PACKAGE  = "nonebot-plugin-parser";
    private const string PARSER_MODULE   = "nonebot_plugin_parser";
    private const string PARSER_VERSION  = "2.7.2";

    // 版本写死：升级需随工具版本一并核实 parser、适配器与 NoneBot 的兼容性。
    private static readonly string[] BasePackages =
    [
        "nonebot2[fastapi,httpx,websockets]==2.5.0",
        "nonebot-adapter-milky==1.3.0",
        $"{PARSER_PACKAGE}=={PARSER_VERSION}"
    ];

    // YouTube 与 TikTok 需要 yt-dlp 与 JS 运行时，本环境不安装，初始配置中禁用。
    private const string INITIAL_ENV = """
        # NB2 插件配置，可在 NB2Bridge 页面的配置表单修改；修改后重启 NB2 生效。
        # 进程级配置（监听地址、Milky 连接、SUPERUSERS、加载的插件等）由 NB2Bridge 经环境变量传入，不写在这里。
        PARSER_DISABLED_PLATFORMS=["youtube", "tiktok"]

        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>该命令接受的选项。</summary>
    public static readonly IReadOnlyCollection<string> Options = [DATA_DIR_OPTION];

    /// <summary>执行初始化。</summary>
    /// <returns>成功时为 0。</returns>
    public static Task<int> RunAsync(ToolContext context)
    {
        string dataDirectory = context.Arguments.Value(DATA_DIR_OPTION) is { Length: > 0 } given
            ? Path.GetFullPath(given)
            : context.Layout.DataDirectory;
        Setup(Path.Combine(dataDirectory, DIRECTORY_NAME), context.Log);
        return Task.FromResult(0);
    }

    /// <summary>在 <paramref name="root" /> 创建或补全 NB2 运行环境。</summary>
    /// <exception cref="ToolException">uv 不可用或命令失败。</exception>
    public static void Setup(string root, ToolLog log)
    {
        (int uvExit, string uvVersion) = CommandRunner.Capture(UV, ["--version"]);
        if (uvExit != 0) throw new ToolException("找不到 uv，请先安装 uv（https://docs.astral.sh/uv/）并确认它在 PATH 中");
        log.Info($"使用 {uvVersion}，NB2 运行环境目录 {root}");

        Directory.CreateDirectory(root);
        string python = PythonPath(root);
        if (!File.Exists(python))
        {
            CommandRunner.Run(
                new SystemCommand
                {
                    FileName    = UV,
                    Arguments   = ["venv", Path.Combine(root, VENV_DIRECTORY), "--python", PYTHON_VERSION],
                    Description = $"创建 Python {PYTHON_VERSION} 虚拟环境"
                },
                log);
        }

        CommandRunner.Run(
            new SystemCommand
            {
                FileName    = UV,
                Arguments   = ["pip", "install", "--python", python, .. BasePackages],
                Description = "安装 NoneBot2、Milky 适配器与 nonebot-plugin-parser"
            },
            log);

        string envPath = Path.Combine(root, ENV_FILE);
        if (!File.Exists(envPath))
        {
            File.WriteAllText(envPath, INITIAL_ENV);
            log.Info($"已写入初始插件配置 {envPath}");
        }

        string pluginsPath = Path.Combine(root, PLUGINS_FILE);
        if (!File.Exists(pluginsPath))
        {
            File.WriteAllText(pluginsPath, InitialPlugins(DateTimeOffset.UtcNow));
            log.Info($"已写入插件清单 {pluginsPath}（{PARSER_PACKAGE} 默认不启用）");
        }

        (int freezeExit, string frozen) = CommandRunner.Capture(UV, ["pip", "freeze", "--python", python]);
        if (freezeExit != 0) throw new ToolException($"读取已安装依赖失败：{frozen}");
        // 输出合并了标准错误，去掉 uv 的提示行，只留依赖。
        string[] requirements =
        [
            .. frozen.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Where(line => !line.StartsWith("warning", StringComparison.OrdinalIgnoreCase))
        ];
        File.WriteAllLines(Path.Combine(root, LOCK_FILE), requirements);
        log.Info("NB2 运行环境已就绪；在面板启用 NB2Bridge 插件后启动 NB2 服务");
    }

    /// <summary>初始插件清单的 JSON：只有 parser，不启用。</summary>
    public static string InitialPlugins(DateTimeOffset installedAt)
    {
        Nb2PluginEntry[] plugins =
        [
            new()
            {
                Package     = PARSER_PACKAGE,
                Module      = PARSER_MODULE,
                Version     = PARSER_VERSION,
                Enabled     = false,
                InstalledAt = installedAt
            }
        ];
        return JsonSerializer.Serialize(plugins, JsonOptions);
    }

    private static string PythonPath(string root) =>
        OperatingSystem.IsWindows()
            ? Path.Combine(root, VENV_DIRECTORY, "Scripts", "python.exe")
            : Path.Combine(root, VENV_DIRECTORY, "bin", "python");
}

/// <summary>
/// NB2 插件清单 <c>data/nb2/plugins.json</c> 的一项，与 NB2Bridge 插件的同名模型保持一致（契约测试核对）。
/// 工具只在首次创建清单时写入初始插件。
/// </summary>
internal sealed class Nb2PluginEntry
{
    /// <summary>PyPI 包名。</summary>
    public required string Package { get; init; }

    /// <summary>NoneBot 加载的顶层模块名。</summary>
    public required string Module { get; init; }

    /// <summary>安装的版本。</summary>
    public required string Version { get; init; }

    /// <summary>上传安装时 <c>wheels/</c> 下的 wheel 文件名；从 PyPI 安装时为 null。</summary>
    public string? Wheel { get; init; }

    /// <summary>是否启用事件响应；新装插件默认不启用。</summary>
    public required bool Enabled { get; init; }

    /// <summary>安装时间（UTC）。</summary>
    public required DateTimeOffset InstalledAt { get; init; }
}
