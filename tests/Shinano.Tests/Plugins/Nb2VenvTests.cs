using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shinano.Framework.Plugins;
using Shinano.Plugin.NB2Bridge.PluginPackages;
using Shinano.Plugin.NB2Bridge.Runtime;
using Shinano.Tests.Support;
using Shinano.Tool.Setup;

namespace Shinano.Tests.Plugins;

/// <summary>
/// NB2 运行环境的依赖固定：状态判定、锁定文件解析、管理员约束校验、随包锁定文件与工具的契约，以及用 uv 实际演练的依赖变更
/// （首次锁定、基础层变化时两阶段重建、试加载失败还原、插件层约束与放宽重试、卸载）。演练需要 uv 与网络，只在设置
/// <c>SHINANO_TEST_NB2_DIR</c> 时运行。
/// </summary>
public sealed class Nb2VenvTests
{
    private const string PYTHON_VERSION = "3.12";

    private static readonly string AssetsDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "nb2");

    [Fact]
    public void Compare_DistinguishesMissingCurrentIncompleteOutdatedAndUnmanaged()
    {
        byte[] expected = "a==1\n"u8.ToArray();

        Assert.Equal(Nb2BaseState.Missing, Nb2Venv.Compare(false, expected, expected, true));
        Assert.Equal(Nb2BaseState.Unmanaged, Nb2Venv.Compare(true, expected, null, true));
        Assert.Equal(Nb2BaseState.Outdated, Nb2Venv.Compare(true, expected, "a==2\n"u8.ToArray(), true));
        Assert.Equal(Nb2BaseState.Incomplete, Nb2Venv.Compare(true, expected, expected, false));
        Assert.Equal(Nb2BaseState.Current, Nb2Venv.Compare(true, expected, expected, true));
        // 只重新生成（头部注释中的 exclude-newer 日期不同）、包与哈希不变时不算基础层变化。
        Assert.Equal(Nb2BaseState.Current, Nb2Venv.Compare(true, "# 2026-11-20\na==1\n"u8.ToArray(), "# 2026-11-21\r\na==1\r\n"u8.ToArray(), true));
    }

    [Fact]
    public void Changes_ComparePinnedVersionsAndIgnoreHashesMarkersAndDirectReferences()
    {
        const string BEFORE = """
            Arclet_Alconna==1.8.44 \
                --hash=sha256:aaaa
                # via nonebot-plugin-alconna
            tarina==0.7.5 ; python_version >= '3.9' \
                --hash=sha256:bbbb
            removed-pkg==1.0
            demo @ file:///C:/x/demo-1.0-py3-none-any.whl \
                --hash=sha256:cccc
            """;
        const string AFTER = """
            arclet-alconna==1.8.44
            tarina==0.8.0
            added-pkg==2.0
            """;

        Assert.Equal("1.8.44", Nb2Venv.LockedVersions(BEFORE)["arclet-alconna"]);
        Assert.False(Nb2Venv.LockedVersions(BEFORE).ContainsKey("demo"));
        Assert.Equal(
            [("added-pkg", null, "2.0"), ("removed-pkg", "1.0", null), ("tarina", "0.7.5", "0.8.0")],
            Nb2Venv.Changes(BEFORE, AFTER).Select(change => (change.Package, change.From, change.To)));
        Assert.Empty(Nb2Venv.Changes(null, AFTER));
    }

    [Theory]
    [InlineData("tarina<0.8")]
    [InlineData("tarina<0.8  # 0.8.0 与 alconna 1.8 不兼容\n# 整行注释\n\nnepattern>=0.7,<1.0")]
    [InlineData("Some_Package[extra1, extra2]==1.2.3 ; sys_platform == 'win32'")]
    [InlineData("")]
    public void Constraints_AcceptNamesVersionsMarkersAndComments(string text) => Assert.Null(Nb2Constraints.Validate(text));

    [Theory]
    [InlineData("--index-url https://example.invalid/simple")]
    [InlineData("-r other.txt")]
    [InlineData("demo @ https://example.invalid/demo.whl")]
    [InlineData("./wheels/demo-1.0-py3-none-any.whl")]
    [InlineData("tarina<0.8\n-e .")]
    public void Constraints_RejectOptionsUrlsAndPaths(string text) => Assert.NotNull(Nb2Constraints.Validate(text));

    [Fact]
    public void UvVersion_ParsedTheSameByToolAndBridge()
    {
        const string OUTPUT = "uv 0.13.9 (6b2d39dcc 2026-11-17 x86_64-pc-windows-msvc)";

        Assert.Equal(new Version(0, 13, 9), Nb2Venv.ParseUvVersion(OUTPUT));
        Assert.Equal(Nb2Venv.ParseUvVersion(OUTPUT), Nb2SetupCommand.ParseUvVersion(OUTPUT));
        Assert.Null(Nb2Venv.ParseUvVersion("uv: command not found"));
    }

    // 随包锁定文件由 build/nb2-lock 生成；输入文件改了而锁定文件没有重新生成、或工具写入的初始插件版本与锁定不一致时失败。
    [Fact]
    public void BundledLocks_MatchInputsAndToolInitialPlugin()
    {
        IReadOnlyDictionary<string, string> baseLock = Nb2Venv.LockedVersions(File.ReadAllText(Path.Combine(AssetsDirectory, "base.lock")));
        IReadOnlyDictionary<string, string> initialLock = Nb2Venv.LockedVersions(File.ReadAllText(Path.Combine(AssetsDirectory, "initial-plugins.lock")));

        foreach ((string name, string version) in Pins("base.in")) Assert.Equal(version, baseLock[name]);
        foreach ((string name, string version) in Pins("initial-plugins.in")) Assert.Equal(version, initialLock[name]);
        Assert.Equal(Nb2SetupCommand.PARSER_VERSION, initialLock["nonebot-plugin-parser"]);
        // 初始插件锁定以基础锁定为约束，两者共有的包版本相同。
        Assert.All(initialLock.Where(pair => baseLock.ContainsKey(pair.Key)), pair => Assert.Equal(baseLock[pair.Key], pair.Value));
        Assert.Equal(Nb2Venv.BASE_LOCK_ASSET, $"nb2/{Nb2SetupCommand.BASE_LOCK_FILE}");
    }

    [Fact]
    public async Task Drill_LocksRebuildsInTwoStagesRestoresOnFailedProbeAndRelaxesOnRequest()
    {
        TestEnvironment.RequireNb2();
        string work = Path.Combine(Path.GetFullPath(TestEnvironment.Nb2Directory!), "drill-" + Guid.NewGuid().ToString("N"));
        string links = Path.Combine(work, "links");
        Directory.CreateDirectory(links);
        string? previousLinks = Environment.GetEnvironmentVariable("UV_FIND_LINKS");
        try
        {
            // 演示依赖：demolib 1.0 要求 httpx<0.28、2.0 要求 httpx>=0.28；otherlib 不依赖基础层，用于插件层约束。
            BuildWheel(work, links, "shinano-demolib", "1.0", "\"httpx<0.28\"", "VERSION = '1.0'");
            BuildWheel(work, links, "shinano-demolib", "2.0", "\"httpx>=0.28\"", "VERSION = '2.0'");
            BuildWheel(work, links, "shinano-otherlib", "1.0", string.Empty, "VERSION = '1.0'");
            BuildWheel(work, links, "shinano-otherlib", "2.0", string.Empty, "VERSION = '2.0'");
            BuildWheel(work, links, "nonebot-plugin-shinano-demob", "1.0", "\"nonebot2>=2.4\", \"shinano-otherlib<2\"", "import shinano_otherlib");
            BuildWheel(work, links, "nonebot-plugin-shinano-demob", "1.1", "\"nonebot2>=2.4\", \"shinano-otherlib<2\"", "import shinano_otherlib");
            string uploads = Path.Combine(work, "uploads");
            Directory.CreateDirectory(uploads);
            string demo = BuildWheel(work, uploads, "nonebot-plugin-shinano-demo", "1.0", "\"nonebot2>=2.4\", \"shinano-demolib\", \"shinano-otherlib\"", "import shinano_demolib, shinano_otherlib");
            string broken = BuildWheel(work, uploads, "nonebot-plugin-shinano-broken", "1.0", "\"nonebot2>=2.4\"", "raise RuntimeError('broken on purpose')");
            Environment.SetEnvironmentVariable("UV_FIND_LINKS", links);

            // 旧基础层：NB2 基础环境加 httpx<0.28，模拟一个旧版 NB2Bridge 的基础锁定文件。
            string oldBaseInput = Path.Combine(work, "old-base.in");
            File.WriteAllText(oldBaseInput, File.ReadAllText(Path.Combine(AssetsDirectory, "base.in")) + "httpx<0.28\n");
            string oldBase = Path.Combine(work, "old-base.lock");
            Run("uv", "pip", "compile", oldBaseInput, "--universal", "--python-version", PYTHON_VERSION, "--generate-hashes", "--prerelease", "disallow", "-o", oldBase);

            Nb2Layout layout = new(Path.Combine(work, "data"));
            Run("uv", "venv", Path.GetDirectoryName(Path.GetDirectoryName(layout.Python))!, "--python", PYTHON_VERSION);
            Directory.CreateDirectory(layout.WheelsDirectory);
            File.Copy(demo, Path.Combine(layout.WheelsDirectory, Path.GetFileName(demo)));
            Nb2Catalog catalog = new(layout);
            catalog.Put(Entry("nonebot-plugin-shinano-demo", "nonebot_plugin_shinano_demo", Path.GetFileName(demo)));

            // 1. 旧环境（没有实际基础锁定文件）重建：安装插件层、试加载并锁定。
            Nb2Venv oldVenv = await VenvAsync(layout, File.ReadAllBytes(oldBase));
            Assert.Equal(Nb2BaseState.Unmanaged, oldVenv.State());
            Nb2ChangeResult first = await oldVenv.RebuildAsync(catalog.Entries());
            Assert.Null(first.Error);
            Assert.Equal(Nb2BaseState.Current, oldVenv.State());
            Assert.Equal("1.0", PluginsLock(layout)["shinano-demolib"]);
            Assert.Contains("--hash=sha256:", File.ReadAllText(layout.PluginsLockFile));

            // 2. NB2Bridge 带来新基础层：第一阶段保留 demolib 1.0 与新 httpx 冲突，第二阶段重新解析为 2.0。
            Nb2Venv venv = await VenvAsync(layout, File.ReadAllBytes(Path.Combine(AssetsDirectory, "base.lock")));
            Assert.Equal(Nb2BaseState.Outdated, venv.State());
            Nb2ChangeResult second = await venv.RebuildAsync(catalog.Entries());
            Assert.Null(second.Error);
            Assert.Contains(second.Changes, change => change is { Package: "shinano-demolib", From: "1.0", To: "2.0" });
            Assert.Equal(Nb2BaseState.Current, venv.State());

            // 3. 基础层未变时按两份锁定文件精确重建，版本不变。
            Nb2ChangeResult third = await venv.RebuildAsync(catalog.Entries());
            Assert.Null(third.Error);
            Assert.Empty(third.Changes);

            // 3b. 升级上传安装的插件：插件层版本约束不含插件自身，严格模式下即可升级。
            string demoUpgrade = BuildWheel(work, uploads, "nonebot-plugin-shinano-demo", "1.1", "\"nonebot2>=2.4\", \"shinano-demolib\", \"shinano-otherlib\"", "import shinano_demolib, shinano_otherlib");
            File.Copy(demoUpgrade, Path.Combine(layout.WheelsDirectory, Path.GetFileName(demoUpgrade)));
            Nb2PluginEntry demoNext = Entry("nonebot-plugin-shinano-demo", "nonebot_plugin_shinano_demo", Path.GetFileName(demoUpgrade), "1.1");
            Nb2ChangeResult upgraded = await venv.InstallAsync(
                demoNext.Package,
                Path.Combine(layout.WheelsDirectory, Path.GetFileName(demoUpgrade)),
                [.. catalog.Entries().Where(entry => entry.Module != demoNext.Module), demoNext],
                relaxInstalled: false);
            Assert.Null(upgraded.Error);
            Assert.Contains(Path.GetFileName(demoUpgrade), File.ReadAllText(layout.PluginsLockFile));
            catalog.Put(demoNext);

            // 4. 试加载失败的插件不被接受，环境还原。
            string lockBefore = File.ReadAllText(layout.PluginsLockFile);
            string brokenTarget = Path.Combine(layout.WheelsDirectory, Path.GetFileName(broken));
            File.Copy(broken, brokenTarget);
            Nb2ChangeResult failed = await venv.InstallAsync(
                "nonebot-plugin-shinano-broken",
                brokenTarget,
                [.. catalog.Entries(), Entry("nonebot-plugin-shinano-broken", "nonebot_plugin_shinano_broken", Path.GetFileName(broken))],
                relaxInstalled: false);
            Assert.Contains("nonebot_plugin_shinano_broken", failed.Error);
            Assert.Equal(lockBefore, File.ReadAllText(layout.PluginsLockFile));
            Assert.DoesNotContain("nonebot-plugin-shinano-broken", Run("uv", "pip", "freeze", "--python", layout.Python));

            // 5. 新插件要求 otherlib<2，与已锁定的 otherlib 2.0 冲突；放宽后接受并列出版本变化。
            Nb2PluginEntry demob = Entry("nonebot-plugin-shinano-demob", "nonebot_plugin_shinano_demob", null);
            Nb2ChangeResult strict = await venv.InstallAsync("nonebot-plugin-shinano-demob", "nonebot-plugin-shinano-demob==1.0", [.. catalog.Entries(), demob], relaxInstalled: false);
            Assert.True(strict is { Error: not null, Conflict: true });
            Assert.Equal(lockBefore, File.ReadAllText(layout.PluginsLockFile));
            Nb2ChangeResult relaxed = await venv.InstallAsync("nonebot-plugin-shinano-demob", "nonebot-plugin-shinano-demob==1.0", [.. catalog.Entries(), demob], relaxInstalled: true);
            Assert.Null(relaxed.Error);
            Assert.Contains(relaxed.Changes, change => change is { Package: "shinano-otherlib", From: "2.0", To: "1.0" });
            catalog.Put(demob);

            // 5b. 升级从索引安装的插件（锁定为 名==版本）：同样不受自身旧版本约束。
            Nb2PluginEntry demobNext = Entry(demob.Package, demob.Module, null, "1.1");
            Nb2ChangeResult upgradedB = await venv.InstallAsync(
                demob.Package,
                "nonebot-plugin-shinano-demob==1.1",
                [.. catalog.Entries().Where(entry => entry.Module != demob.Module), demobNext],
                relaxInstalled: false);
            Assert.Null(upgradedB.Error);
            Assert.Contains(upgradedB.Changes, change => change is { Package: "nonebot-plugin-shinano-demob", From: "1.0", To: "1.1" });
            catalog.Put(demobNext);

            // 6. 卸载后试加载剩余插件并重新锁定。
            Nb2ChangeResult removed = await venv.UninstallAsync(demob.Package, [.. catalog.Entries().Where(entry => entry.Module != demob.Module)]);
            Assert.Null(removed.Error);
            Assert.False(PluginsLock(layout).ContainsKey("nonebot-plugin-shinano-demob"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("UV_FIND_LINKS", previousLinks);
            Directory.Delete(work, recursive: true);
        }
    }

    // 新部署：工具只建立基础层；NB2Bridge 发现插件层未安装（启动前自动执行）后按随附锁定安装 parser，并实际加载。
    [Fact]
    public async Task Drill_ToolSetupThenFirstRebuildInstallsInitialPluginFromBundledLock()
    {
        TestEnvironment.RequireNb2();
        string data = Path.Combine(Path.GetFullPath(TestEnvironment.Nb2Directory!), "setup-" + Guid.NewGuid().ToString("N"));
        try
        {
            // 插件目录中放一个只含清单与基础锁定文件的 NB2Bridge，工具从这里读取期望值。
            string bridge = Path.Combine(data, "plugins", "nb2-bridge");
            Directory.CreateDirectory(Path.Combine(bridge, "assets", "nb2"));
            File.WriteAllText(Path.Combine(bridge, "plugin.json"), $$"""{ "id": "{{Nb2SetupCommand.BRIDGE_PLUGIN_ID}}" }""");
            File.Copy(Path.Combine(AssetsDirectory, "base.lock"), Path.Combine(bridge, "assets", "nb2", "base.lock"));

            Nb2SetupCommand.Setup(data, new Shinano.Tool.Deployment.ToolLog(Path.Combine(data, "tool.log"), null));
            Nb2Layout layout = new(data);
            Nb2Venv venv = await VenvAsync(layout, File.ReadAllBytes(Path.Combine(AssetsDirectory, "base.lock")));
            Assert.Equal(Nb2BaseState.Incomplete, venv.State());

            Nb2ChangeResult result = await venv.RebuildAsync(new Nb2Catalog(layout).Entries());
            Assert.Null(result.Error);
            Assert.Equal(Nb2BaseState.Current, venv.State());
            IReadOnlyDictionary<string, string> locked = PluginsLock(layout);
            Assert.Equal(Nb2SetupCommand.PARSER_VERSION, locked["nonebot-plugin-parser"]);
            Assert.Equal(Nb2Venv.LockedVersions(File.ReadAllText(Path.Combine(AssetsDirectory, "initial-plugins.lock")))["tarina"], locked["tarina"]);

            // 重跑工具不改动已有环境。
            Nb2SetupCommand.Setup(data, new Shinano.Tool.Deployment.ToolLog(Path.Combine(data, "tool.log"), null));
            Assert.Equal(Nb2BaseState.Current, venv.State());
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    private static IEnumerable<(string Name, string Version)> Pins(string inputFile) =>
        Nb2Venv.LockedVersions(File.ReadAllText(Path.Combine(AssetsDirectory, inputFile)).Replace("[fastapi,httpx,websockets]", string.Empty))
               .Select(pair => (pair.Key, pair.Value));

    private static IReadOnlyDictionary<string, string> PluginsLock(Nb2Layout layout) => Nb2Venv.LockedVersions(File.ReadAllText(layout.PluginsLockFile));

    private static Nb2PluginEntry Entry(string package, string module, string? wheel, string version = "1.0") =>
        new()
        {
            Package     = package,
            Module      = module,
            Version     = version,
            Wheel       = wheel,
            Enabled     = false,
            InstalledAt = DateTimeOffset.UnixEpoch
        };

    private static async Task<Nb2Venv> VenvAsync(Nb2Layout layout, byte[] expectedBase)
    {
        Nb2Venv venv = new(new DrillContext(expectedBase), layout, NullLogger<Nb2Venv>.Instance);
        await venv.LoadAsync(CancellationToken.None);
        return venv;
    }

    // 现场构建一个纯 Python 包的 wheel，返回 wheel 路径。
    private static string BuildWheel(string work, string output, string name, string version, string dependencies, string source)
    {
        string module = name.Replace('-', '_');
        string project = Path.Combine(work, "src", $"{name}-{version}");
        Directory.CreateDirectory(Path.Combine(project, "src", module));
        File.WriteAllText(
            Path.Combine(project, "pyproject.toml"),
            $"""
             [project]
             name = "{name}"
             version = "{version}"
             requires-python = ">=3.12"
             dependencies = [{dependencies}]
             [build-system]
             requires = ["uv_build>=0.9,<0.20"]
             build-backend = "uv_build"
             """);
        File.WriteAllText(Path.Combine(project, "src", module, "__init__.py"), source + "\n");
        Run("uv", "build", "--wheel", "-o", output, project);
        return Path.Combine(output, $"{module}-{version}-py3-none-any.whl");
    }

    private static string Run(string fileName, params string[] arguments)
    {
        ProcessStartInfo start = new(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"无法启动 {fileName}");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{fileName} {string.Join(' ', arguments)} 失败：{output}{error.Result}");
        return output;
    }

    // 演练只用到插件资产：期望基础锁定文件由测试指定，其余取自随包资产。
    private sealed class DrillContext : IPluginInitializationContext, IPluginAssets
    {
        private readonly byte[] _expectedBase;

        public DrillContext(byte[] expectedBase) => _expectedBase = expectedBase;

        public Task<byte[]> ReadAllBytesAsync(string relativePath, CancellationToken cancellationToken) =>
            relativePath == Nb2Venv.BASE_LOCK_ASSET
                ? Task.FromResult(_expectedBase)
                : File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "assets", relativePath), cancellationToken);

        public string PluginId => "nb2-bridge";

        public IPluginDatabase Database => throw new NotSupportedException();

        public IPluginEvents Events => throw new NotSupportedException();

        public IPluginScheduler Scheduler => throw new NotSupportedException();

        public IPluginMedia Media => throw new NotSupportedException();

        public IPluginBuckets Buckets => throw new NotSupportedException();

        public IBotAccessor Bot => throw new NotSupportedException();

        public IPluginAssets Assets => this;

        public IPluginPanel Panel => throw new NotSupportedException();

        public IPluginFonts Fonts => throw new NotSupportedException();

        public IPluginImages Images => throw new NotSupportedException();

        public IPluginBrowser Browser => throw new NotSupportedException();

        public IPluginTools Tools => throw new NotSupportedException();

        public string DataDirectory => throw new NotSupportedException();

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public void OnEnabledChanged(Action<bool> handler) => throw new NotSupportedException();

        public IPluginNotices Notices => throw new NotSupportedException();

        public IPluginConfig<T> Config<T>() where T : class, new() => throw new NotSupportedException();

        public IPluginSecrets<T> Secrets<T>() where T : class, new() => throw new NotSupportedException();
    }
}
