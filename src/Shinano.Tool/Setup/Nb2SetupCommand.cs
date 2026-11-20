using System.Text.Json;
using System.Text.RegularExpressions;
using Shinano.Tool.Cli;
using Shinano.Tool.Configuration;
using Shinano.Tool.Deployment;
using Shinano.Tool.Plugins;
using Shinano.Tool.Services;

namespace Shinano.Tool.Setup;

/// <summary>
/// <c>nb2-setup</c>：首次建立 NB2 运行环境。用 uv 在部署数据目录的 <c>nb2/</c> 下创建 Python 3.12 venv，按已安装的 NB2Bridge
/// 插件随附的基础锁定文件（<c>assets/nb2/base.lock</c>）带哈希同步 NB2 基础环境，把它复制为部署中的实际基础锁定文件，
/// 并写入初始插件配置 <c>.env</c> 与插件清单。插件层（含初始插件 parser）由 NB2Bridge 在首次启动 NB2 前自动安装并试加载；
/// 之后的依赖变更（安装、卸载、按新版基础锁定文件重建）都在 NB2Bridge 页面进行。venv 已存在时只补写缺失的 <c>.env</c> 与插件清单。
/// <c>bot.py</c> 与控制插件由 NB2Bridge 每次启动 NB2 前写入，不在这里放置。
/// </summary>
public static partial class Nb2SetupCommand
{
    /// <summary>NB2 运行环境所在目录名，位于部署数据目录下；与 NB2Bridge 一致。</summary>
    public const string DIRECTORY_NAME = "nb2";

    /// <summary>插件清单文件名；与 NB2Bridge 一致。</summary>
    public const string PLUGINS_FILE = "plugins.json";

    /// <summary>实际基础锁定文件名，也是 NB2Bridge 资产中的期望基础锁定文件名；与 NB2Bridge 一致。</summary>
    public const string BASE_LOCK_FILE = "base.lock";

    /// <summary>插件层锁定文件名；首次建立时删除残留的旧文件，由 NB2Bridge 重新生成。与 NB2Bridge 一致。</summary>
    public const string PLUGINS_LOCK_FILE = "plugins.lock";

    /// <summary>NB2Bridge 的插件 ID。</summary>
    public const string BRIDGE_PLUGIN_ID = "nb2-bridge";

    /// <summary>初始插件 parser 的版本，须与 NB2Bridge 资产 <c>initial-plugins.in</c> 一致（契约测试核对）。</summary>
    public const string PARSER_VERSION = "2.7.3";

    /// <summary>部署端 uv 命令所需的最低版本（实测 0.5.0 可用）。</summary>
    public static readonly Version MinimumUvVersion = new(0, 5, 0);

    private const string DATA_DIR_OPTION = "data-dir";
    private const string VENV_DIRECTORY  = ".venv";
    private const string ENV_FILE        = ".env";
    private const string UV              = "uv";
    private const string PYTHON_VERSION  = "3.12";
    private const string PARSER_PACKAGE  = "nonebot-plugin-parser";
    private const string PARSER_MODULE   = "nonebot_plugin_parser";
    private const string ASSETS_PATH     = "assets/nb2";

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
        Setup(dataDirectory, context.Log);
        return Task.FromResult(0);
    }

    /// <summary>在部署数据目录 <paramref name="dataDirectory" /> 下建立 NB2 运行环境。</summary>
    /// <exception cref="ToolException">uv 不可用或版本过低、未安装 NB2Bridge，或命令失败。</exception>
    public static void Setup(string dataDirectory, ToolLog log)
    {
        string root = Path.Combine(dataDirectory, DIRECTORY_NAME);
        CheckUv(log);
        string expectedLock = ExpectedBaseLock(dataDirectory);

        Directory.CreateDirectory(root);
        string venv = Path.Combine(root, VENV_DIRECTORY);
        string python = PythonPath(root);
        if (File.Exists(python))
        {
            log.Info($"NB2 运行环境已存在（{venv}），不改动依赖；按新版基础锁定文件更新或修复环境请在 NB2Bridge 页面执行重建");
        }
        else
        {
            CreateEnvironment(root, venv, python, expectedLock, log);
        }

        string envPath = Path.Combine(root, ENV_FILE);
        if (!File.Exists(envPath))
        {
            PrivateFile.Write(envPath, INITIAL_ENV);
            log.Info($"已写入初始插件配置 {envPath}");
        }

        string pluginsPath = Path.Combine(root, PLUGINS_FILE);
        if (!File.Exists(pluginsPath))
        {
            File.WriteAllText(pluginsPath, InitialPlugins(DateTimeOffset.UtcNow));
            log.Info($"已写入插件清单 {pluginsPath}（{PARSER_PACKAGE} 默认不启用）");
        }

        log.Info("NB2 运行环境已就绪；在面板启用 NB2Bridge 插件后，首次启动 NB2 前会自动安装插件层");
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

    /// <summary>从 <c>uv --version</c> 的输出（如 <c>uv 0.13.9 (6b2d39dcc 2026-11-17 …)</c>）中取版本号；无法识别时为 null。</summary>
    public static Version? ParseUvVersion(string output) =>
        UvVersionPattern().Match(output) is { Success: true } match ? Version.Parse(match.Groups["version"].Value) : null;

    private static void CheckUv(ToolLog log)
    {
        (int exitCode, string output) = CommandRunner.Capture(UV, ["--version"]);
        if (exitCode != 0) throw new ToolException("找不到 uv，请先安装 uv（https://docs.astral.sh/uv/）并确认它在 PATH 中");
        if (ParseUvVersion(output) is not { } version || version < MinimumUvVersion)
            throw new ToolException($"uv 版本过低或无法识别（{output.Trim()}），需要 {MinimumUvVersion} 或更高版本，可运行 uv self update 升级");
        log.Info($"使用 {output.Trim()}");
    }

    // 期望值随 NB2Bridge 插件包发布，取自已安装的 NB2Bridge，使工具与正在运行的 NB2Bridge 使用同一份。
    private static string ExpectedBaseLock(string dataDirectory)
    {
        LocalConfig config = LocalConfig.Load(Path.Combine(dataDirectory, DeploymentLayout.LOCAL_CONFIG_FILE_NAME));
        string pluginRoot = Path.GetFullPath(config.GetString(ConfigKeys.PLUGIN_DIRECTORY) ?? ConfigKeys.DEFAULT_PLUGIN_DIRECTORY, dataDirectory);
        if (!PluginPackages.ScanInstalled(pluginRoot).TryGetValue(BRIDGE_PLUGIN_ID, out InstalledPlugin? bridge))
            throw new ToolException($"插件目录 {pluginRoot} 中没有安装 NB2Bridge，无法取得 NB2 基础锁定文件；请先在面板安装 NB2Bridge 插件包");

        string path = Path.Combine(bridge.Directory, ASSETS_PATH, BASE_LOCK_FILE);
        if (!File.Exists(path)) throw new ToolException($"已安装的 NB2Bridge 缺少基础锁定文件 {path}，请升级到新版 NB2Bridge");
        return path;
    }

    // venv 存在即表示基础层已按锁定文件同步：同步失败时删除本次创建的 venv，下次重新创建。
    private static void CreateEnvironment(string root, string venv, string python, string expectedLock, ToolLog log)
    {
        log.Info($"建立 NB2 运行环境 {root}");
        CommandRunner.Run(
            new SystemCommand
            {
                FileName    = UV,
                Arguments   = ["venv", venv, "--python", PYTHON_VERSION],
                Description = $"创建 Python {PYTHON_VERSION} 虚拟环境"
            },
            log);
        try
        {
            CommandRunner.Run(
                new SystemCommand
                {
                    FileName    = UV,
                    Arguments   = ["pip", "sync", "--python", python, "--require-hashes", expectedLock],
                    Description = "按基础锁定文件安装 NoneBot2 与 Milky 适配器"
                },
                log);
        }
        catch (ToolException)
        {
            Directory.Delete(venv, recursive: true);
            log.Error($"安装 NB2 基础环境失败，已删除未完成的 venv {venv}");
            throw;
        }

        File.Copy(expectedLock, Path.Combine(root, BASE_LOCK_FILE), overwrite: true);
        // 新 venv 中没有插件；残留的插件层锁定文件会使 NB2Bridge 误以为插件层已就绪。
        File.Delete(Path.Combine(root, PLUGINS_LOCK_FILE));
    }

    private static string PythonPath(string root) =>
        OperatingSystem.IsWindows()
            ? Path.Combine(root, VENV_DIRECTORY, "Scripts", "python.exe")
            : Path.Combine(root, VENV_DIRECTORY, "bin", "python");

    [GeneratedRegex(@"\buv (?<version>\d+\.\d+\.\d+)")]
    private static partial Regex UvVersionPattern();
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
