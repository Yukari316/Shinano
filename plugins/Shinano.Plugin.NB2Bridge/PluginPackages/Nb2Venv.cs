using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.NB2Bridge.Runtime;

namespace Shinano.Plugin.NB2Bridge.PluginPackages;

/// <summary>
/// NB2 运行环境（venv）的依赖变更。分两层固定：
/// <list type="bullet">
/// <item>基础层：NB2Bridge 资产中的期望基础锁定文件（跨平台、带哈希），同步后复制为部署中的实际基础锁定文件。</item>
/// <item>插件层：每次变更通过试加载后，把全部插件的依赖闭包按已安装的版本生成带哈希的插件层锁定文件。</item>
/// </list>
/// 插件安装以实际基础锁定文件、插件层锁定文件与管理员约束为约束，并禁止预发布版；基础层与已验证插件的依赖都不会被新插件改变。
/// 每次变更后在一个进程中试加载全部插件（<c>bot.py</c> 的试加载模式），失败即还原：有两份锁定文件时按它们带哈希同步，
/// 否则（旧环境）按变更前的 <c>uv pip freeze</c> 同步。全部操作要求 NB2 已停止，由调用方串行执行。
/// </summary>
public sealed partial class Nb2Venv
{
    /// <summary>期望基础锁定文件在插件资产中的路径。</summary>
    public const string BASE_LOCK_ASSET = "nb2/base.lock";

    /// <summary>首次建立时随附插件的锁定文件在插件资产中的路径。</summary>
    public const string INITIAL_LOCK_ASSET = "nb2/initial-plugins.lock";

    private const string UV               = "uv";
    private const string BOT_ASSET        = "nb2/bot.py";
    private const string CONTROL_ASSET    = "nb2/control/shinano_nb2_control/__init__.py";
    private const string CONTROL_PACKAGE  = "shinano_nb2_control";
    private const string CONTROL_INIT     = "__init__.py";
    private const string PROBE_MARKER     = "SHINANO_NB2_PROBE_FAILED=";
    private const string TEMPORARY_PREFIX = ".change-";
    private const int    OUTPUT_TAIL      = 800;

    /// <summary>一次依赖变更（含失败后的还原）的时长上限。</summary>
    public static readonly TimeSpan ChangeTimeout = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMinutes(2);

    /// <summary>uv 命令所需的最低版本（实测 0.5.0 可用），与 <c>Shinano.Tool nb2-setup</c> 一致。</summary>
    public static readonly Version MinimumUvVersion = new(0, 5, 0);

    private readonly IPluginInitializationContext _context;
    private readonly Nb2Layout                    _layout;
    private readonly ILogger<Nb2Venv>             _logger;
    private          byte[]?                      _expectedBase;

    /// <summary>创建环境访问。</summary>
    public Nb2Venv(IPluginInitializationContext context, Nb2Layout layout, ILogger<Nb2Venv> logger)
    {
        _context = context;
        _layout  = layout;
        _logger  = logger;
    }

    /// <summary>读取期望基础锁定文件。插件初始化时调用一次，资产在进程内不变。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken) =>
        _expectedBase = await _context.Assets.ReadAllBytesAsync(BASE_LOCK_ASSET, cancellationToken);

    /// <summary>环境与期望基础锁定文件的关系。</summary>
    public Nb2BaseState State() =>
        Compare(
            File.Exists(_layout.Python),
            ExpectedBase(),
            File.Exists(_layout.BaseLockFile) ? File.ReadAllBytes(_layout.BaseLockFile) : null,
            File.Exists(_layout.PluginsLockFile));

    /// <summary>按文件内容判定环境状态。</summary>
    public static Nb2BaseState Compare(bool hasVenv, byte[] expected, byte[]? actual, bool pluginsLocked) =>
        (hasVenv, actual) switch
        {
            (false, _)                                                  => Nb2BaseState.Missing,
            (_, null)                                                   => Nb2BaseState.Unmanaged,
            (_, { } bytes) when !SameLock(bytes, expected) => Nb2BaseState.Outdated,
            _ when !pluginsLocked                          => Nb2BaseState.Incomplete,
            _                                              => Nb2BaseState.Current
        };

    /// <summary>
    /// 两份锁定文件是否锁定同样的内容：忽略 <c>#</c> 注释行与行尾差异。uv 在头部注释记录生成命令（含 exclude-newer 日期），
    /// 只重新生成而包与哈希不变时不算基础层变化。
    /// </summary>
    public static bool SameLock(byte[] left, byte[] right) => LockBody(left).SequenceEqual(LockBody(right));

    /// <summary>从插件资产覆盖 <c>bot.py</c> 与控制插件；启动 NB2 与试加载前调用。</summary>
    public async Task WriteScriptsAsync(CancellationToken cancellationToken)
    {
        string package = Path.Combine(_layout.ControlDirectory, CONTROL_PACKAGE);
        Directory.CreateDirectory(package);
        await File.WriteAllBytesAsync(_layout.BotScript, await _context.Assets.ReadAllBytesAsync(BOT_ASSET, cancellationToken), cancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(package, CONTROL_INIT),
            await _context.Assets.ReadAllBytesAsync(CONTROL_ASSET, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// 安装或升级一个插件。<paramref name="package" /> 是该插件的包名，<paramref name="plugins" /> 是变更后的完整插件清单（含本插件）。
    /// 已锁定的插件层版本作为约束，但不含本插件自身，否则升级会与锁定的旧版本冲突。<paramref name="relaxInstalled" /> 为 true 时
    /// 不以插件层版本为约束，允许调整已装插件的依赖，结果列出版本变化。要求环境为 <see cref="Nb2BaseState.Current" />。
    /// </summary>
    public Task<Nb2ChangeResult> InstallAsync(string package, string requirement, IReadOnlyList<Nb2PluginEntry> plugins, bool relaxInstalled) =>
        ChangeAsync(
            async (work, token) =>
            {
                string? previous = TryRead(_layout.PluginsLockFile);
                string? pins = null;
                if (!relaxInstalled && previous is not null)
                {
                    pins = Path.Combine(work, "installed-pins.txt");
                    string self = Normalize(package);
                    await File.WriteAllLinesAsync(
                        pins,
                        LockedVersions(previous).Where(pair => pair.Key != self).Select(pair => $"{pair.Key}=={pair.Value}"),
                        token);
                }

                List<string> arguments = ["pip", "install", "--python", _layout.Python, .. Constraints(_layout.BaseLockFile, pins)];
                (int exitCode, string output) = await RunAsync(UV, [.. arguments, requirement], token);
                if (exitCode != 0)
                {
                    if (relaxInstalled) return Failure("依赖与 NB2 基础环境冲突或无法安装", output);
                    Nb2ChangeResult conflict = Failure("依赖与 NB2 基础环境或已装插件冲突，或无法安装", output);
                    return new Nb2ChangeResult
                    {
                        Error    = conflict.Error,
                        Conflict = true
                    };
                }

                return await FinishAsync(work, plugins, _layout.BaseLockFile, token, previous: previous);
            });

    /// <summary>卸载一个插件。<paramref name="plugins" /> 是卸载后剩余的插件清单。卸载不清理其他插件可能共用的依赖。</summary>
    public Task<Nb2ChangeResult> UninstallAsync(string package, IReadOnlyList<Nb2PluginEntry> plugins) =>
        ChangeAsync(
            async (work, token) =>
            {
                string? previous = TryRead(_layout.PluginsLockFile);
                (int exitCode, string output) = await RunAsync(UV, ["pip", "uninstall", "--python", _layout.Python, package], token);
                if (exitCode != 0) return Failure($"卸载 {package} 失败", output);

                // 旧环境（没有实际基础锁定文件）不生成插件层锁定文件，等待管理员重建。
                return File.Exists(_layout.BaseLockFile)
                    ? await FinishAsync(work, plugins, _layout.BaseLockFile, token, previous: previous)
                    : await ProbeAsync(plugins, work, token) is { } failure ? failure : Nb2ChangeResult.Success([]);
            });

    /// <summary>
    /// 按期望基础锁定文件重建环境：基础层带哈希同步；插件层按情况处理——基础层未变且已锁定时按两份锁定文件精确同步；
    /// 尚未锁定（首次建立或旧环境）时安装插件清单，parser 版本与随附锁定一致时按其锁定安装；基础层已变时先以旧插件层
    /// 版本为约束安装（第一阶段），失败或试加载失败再去掉旧版本约束重新解析（第二阶段）。成功后写入两份锁定文件，
    /// 结果列出插件层的版本变化；最终失败时整体还原。
    /// </summary>
    public Task<Nb2ChangeResult> RebuildAsync(IReadOnlyList<Nb2PluginEntry> plugins) =>
        ChangeAsync(
            async (work, token) =>
            {
                string expected = Path.Combine(work, Nb2Layout.BASE_LOCK_FILE);
                await File.WriteAllBytesAsync(expected, ExpectedBase(), token);
                string? oldPlugins = File.Exists(_layout.PluginsLockFile) ? await File.ReadAllTextAsync(_layout.PluginsLockFile, token) : null;
                bool baseUnchanged = File.Exists(_layout.BaseLockFile)
                                     && SameLock(await File.ReadAllBytesAsync(_layout.BaseLockFile, token), ExpectedBase());

                if (oldPlugins is not null && baseUnchanged)
                {
                    (int exitCode, string output) = await RunAsync(
                        UV,
                        ["pip", "sync", "--python", _layout.Python, "--require-hashes", expected, _layout.PluginsLockFile],
                        token);
                    if (exitCode == 0 && await ProbeAsync(plugins, work, token) is null)
                    {
                        _logger.LogInformation("已按两份锁定文件重建 NB2 环境，版本未变");
                        return Nb2ChangeResult.Success([]);
                    }

                    _logger.LogWarning("按两份锁定文件重建 NB2 环境失败，改为重新安装插件层：{Output}", Tail(output));
                }

                if (await SyncBaseAsync(expected, token) is { } baseFailure) return baseFailure;
                if (oldPlugins is null)
                {
                    if (await InstallInitialAsync(plugins, expected, work, token) is { } initialFailure) return initialFailure;
                    return await FinishAsync(work, plugins, expected, token, commitBase: true);
                }

                // 第一阶段：保留旧插件层中不属于新基础层的版本。
                string pins = Path.Combine(work, "pins.txt");
                IReadOnlyDictionary<string, string> basePackages = LockedVersions(Encoding.UTF8.GetString(ExpectedBase()));
                await File.WriteAllLinesAsync(
                    pins,
                    LockedVersions(oldPlugins).Where(pair => !basePackages.ContainsKey(pair.Key)).Select(pair => $"{pair.Key}=={pair.Value}"),
                    token);
                Nb2ChangeResult? first = await InstallPluginsAsync(plugins, expected, ["-c", pins], token);
                first ??= await ProbeAsync(plugins, work, token);
                if (first is null) return await FinishAsync(work, plugins, expected, token, commitBase: true, previous: oldPlugins);

                // 第二阶段：去掉旧版本约束重新解析，仍以试加载把关。
                _logger.LogWarning("保留原版本重建 NB2 插件层失败，改为重新解析：{Error}", first.Error);
                if (await SyncBaseAsync(expected, token) is { } retryFailure) return retryFailure;
                if (await InstallPluginsAsync(plugins, expected, [], token) is { } secondFailure) return secondFailure;
                return await FinishAsync(work, plugins, expected, token, commitBase: true, previous: oldPlugins);
            });

    /// <summary>锁定文件中按包名（PEP 503 规范化）索引的版本；只取 <c>名==版本</c> 形式的行，直接引用的本地 wheel 不计入。</summary>
    public static IReadOnlyDictionary<string, string> LockedVersions(string lockText) =>
        lockText.Split('\n')
                .Select(line => PinnedPattern().Match(line))
                .Where(match => match.Success)
                .GroupBy(match => Normalize(match.Groups["name"].Value))
                .ToDictionary(group => group.Key, group => group.First().Groups["version"].Value);

    /// <summary>新旧插件层锁定文件之间的版本变化，按包名排列；新增或删除的包另一侧为 null。</summary>
    public static IReadOnlyList<Nb2VersionChange> Changes(string? previous, string current)
    {
        if (previous is null) return [];
        IReadOnlyDictionary<string, string> before = LockedVersions(previous);
        IReadOnlyDictionary<string, string> after = LockedVersions(current);
        return
        [
            .. before.Keys.Union(after.Keys)
                     .Order(StringComparer.Ordinal)
                     .Select(name => new Nb2VersionChange
                     {
                         Package = name,
                         From    = before.GetValueOrDefault(name),
                         To      = after.GetValueOrDefault(name)
                     })
                     .Where(change => change.From != change.To)
        ];
    }

    private byte[] ExpectedBase() => _expectedBase ?? throw new InvalidOperationException("期望基础锁定文件尚未读取");

    // 执行一次依赖变更：先检查 uv 并确定还原方式，变更不随请求取消、只受时长上限限制；失败或超时都还原，工作目录用完即删。
    private async Task<Nb2ChangeResult> ChangeAsync(Func<string, CancellationToken, Task<Nb2ChangeResult>> change)
    {
        if (!File.Exists(_layout.Python)) return Nb2ChangeResult.Failed("未找到 NB2 运行环境，请在部署机运行 scripts 下的 nb2-setup");

        string work = Path.Combine(_layout.Root, TEMPORARY_PREFIX + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        using CancellationTokenSource timeout = new(ChangeTimeout);
        try
        {
            string? snapshot;
            try
            {
                if (await UvErrorAsync(timeout.Token) is { } uvError) return Nb2ChangeResult.Failed(uvError);
                snapshot = await SnapshotAsync(work, timeout.Token);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "准备 NB2 依赖变更失败，未做任何变更");
                return Nb2ChangeResult.Failed($"准备失败，未做任何变更：{exception.Message}");
            }

            try
            {
                Nb2ChangeResult result = await change(work, timeout.Token);
                if (result.Error is not null) await RestoreAsync(snapshot);
                return result;
            }
            catch (Exception exception)
            {
                bool timedOut = timeout.IsCancellationRequested;
                _logger.LogError(exception, "NB2 依赖变更{Reason}，还原依赖", timedOut ? $"超过 {ChangeTimeout.TotalMinutes:0} 分钟未完成" : "失败");
                await RestoreAsync(snapshot);
                return Nb2ChangeResult.Failed(timedOut ? $"超过 {ChangeTimeout.TotalMinutes:0} 分钟未完成，已中止并还原" : $"失败并已还原：{exception.Message}");
            }
        }
        finally
        {
            TryDelete(work);
        }
    }

    /// <summary>uv 不可用或低于 <see cref="MinimumUvVersion" /> 时的说明；可用时为 null。</summary>
    private async Task<string?> UvErrorAsync(CancellationToken cancellationToken)
    {
        (int exitCode, string output) = await RunAsync(UV, ["--version"], cancellationToken);
        if (exitCode != 0) return "找不到 uv，请在部署机安装 uv 并确认它在宿主进程的 PATH 中";
        return ParseUvVersion(output) is { } version && version >= MinimumUvVersion
            ? null
            : $"uv 版本过低或无法识别（{output}），需要 {MinimumUvVersion} 或更高版本";
    }

    /// <summary>从 <c>uv --version</c> 的输出中取版本号；无法识别时为 null。</summary>
    public static Version? ParseUvVersion(string output) =>
        UvVersionPattern().Match(output) is { Success: true } match ? Version.Parse(match.Groups["version"].Value) : null;

    // 两份锁定文件都在时它们就是变更前的状态（变更成功才覆盖）；旧环境另存一份 freeze。
    private async Task<string?> SnapshotAsync(string work, CancellationToken cancellationToken)
    {
        if (File.Exists(_layout.BaseLockFile) && File.Exists(_layout.PluginsLockFile)) return null;

        (int exitCode, string output) = await RunAsync(UV, ["pip", "freeze", "--python", _layout.Python], cancellationToken, includeErrors: false);
        if (exitCode != 0) throw new IOException("读取当前已安装的依赖失败");
        string path = Path.Combine(work, "snapshot.txt");
        await File.WriteAllTextAsync(path, output, cancellationToken);
        return path;
    }

    // 还原有自己的时长上限，不受已超时的变更影响。
    private async Task RestoreAsync(string? snapshot)
    {
        using CancellationTokenSource timeout = new(ChangeTimeout);
        IReadOnlyList<string> arguments = snapshot is null
            ? ["pip", "sync", "--python", _layout.Python, "--require-hashes", _layout.BaseLockFile, _layout.PluginsLockFile]
            : ["pip", "sync", "--python", _layout.Python, snapshot];
        try
        {
            (int exitCode, string output) = await RunAsync(UV, arguments, timeout.Token);
            if (exitCode == 0) _logger.LogInformation("已还原 NB2 依赖");
            else _logger.LogError("还原 NB2 依赖失败（退出码 {ExitCode}），请在 NB2 页面重建环境：{Output}", exitCode, Tail(output));
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("还原 NB2 依赖超时，NB2 运行环境可能不完整，请在 NB2 页面重建环境");
        }
    }

    private async Task<Nb2ChangeResult?> SyncBaseAsync(string expected, CancellationToken cancellationToken)
    {
        (int exitCode, string output) = await RunAsync(UV, ["pip", "sync", "--python", _layout.Python, "--require-hashes", expected], cancellationToken);
        return exitCode == 0 ? null : Failure("按基础锁定文件同步失败", output);
    }

    // 尚未锁定时：parser 版本与随附锁定一致则按其带哈希安装，其余插件再以它为约束安装。
    private async Task<Nb2ChangeResult?> InstallInitialAsync(
        IReadOnlyList<Nb2PluginEntry> plugins,
        string                        expected,
        string                        work,
        CancellationToken             cancellationToken)
    {
        string initialText = Encoding.UTF8.GetString(await _context.Assets.ReadAllBytesAsync(INITIAL_LOCK_ASSET, cancellationToken));
        IReadOnlyDictionary<string, string> initialVersions = LockedVersions(initialText);
        List<Nb2PluginEntry> initial =
        [
            .. plugins.Where(entry => entry.Wheel is null
                                      && initialVersions.TryGetValue(Normalize(entry.Package), out string? version)
                                      && version == entry.Version)
        ];
        List<string> extra = [];
        if (initial.Count > 0)
        {
            string initialLock = Path.Combine(work, "initial-plugins.lock");
            await File.WriteAllTextAsync(initialLock, initialText, cancellationToken);
            (int exitCode, string output) = await RunAsync(
                UV,
                ["pip", "install", "--python", _layout.Python, "--require-hashes", "-r", initialLock, "-c", expected],
                cancellationToken);
            if (exitCode != 0) return Failure("按随附锁定文件安装初始插件失败", output);
            extra = ["-c", initialLock];
        }

        // 试加载由调用方随后的 FinishAsync 执行。
        return await InstallPluginsAsync([.. plugins.Except(initial)], expected, extra, cancellationToken);
    }

    private async Task<Nb2ChangeResult?> InstallPluginsAsync(
        IReadOnlyList<Nb2PluginEntry> plugins,
        string                        baseLock,
        IReadOnlyList<string>         extraConstraints,
        CancellationToken             cancellationToken)
    {
        if (plugins.Count == 0) return null;
        (int exitCode, string output) = await RunAsync(
            UV,
            ["pip", "install", "--python", _layout.Python, .. Constraints(baseLock, null), .. extraConstraints, .. plugins.Select(Requirement)],
            cancellationToken);
        return exitCode == 0 ? null : Failure("安装插件层失败", output);
    }

    // 试加载通过后生成插件层锁定文件；commitBase 时同时把期望基础锁定文件写为实际值。
    private async Task<Nb2ChangeResult> FinishAsync(
        string                        work,
        IReadOnlyList<Nb2PluginEntry> plugins,
        string                        baseLock,
        CancellationToken             cancellationToken,
        bool                          commitBase = false,
        string?                       previous   = null)
    {
        if (await ProbeAsync(plugins, work, cancellationToken) is { } failure) return failure;

        string pluginsLock = Path.Combine(work, Nb2Layout.PLUGINS_LOCK_FILE);
        if (plugins.Count == 0)
        {
            await File.WriteAllTextAsync(pluginsLock, string.Empty, cancellationToken);
        }
        else
        {
            (int freezeExit, string frozen) = await RunAsync(UV, ["pip", "freeze", "--python", _layout.Python], cancellationToken, includeErrors: false);
            if (freezeExit != 0) return Failure("读取已安装的依赖失败", frozen);
            string installed = Path.Combine(work, "installed.txt");
            string requirements = Path.Combine(work, "plugins.in");
            await File.WriteAllTextAsync(installed, frozen, cancellationToken);
            await File.WriteAllLinesAsync(requirements, plugins.Select(Requirement), cancellationToken);
            // 以当前已安装的版本为约束解析，结果就是已验证的插件层，并带上哈希。
            (int exitCode, string output) = await RunAsync(
                UV,
                [
                    "pip", "compile", requirements, "--python", _layout.Python, "--generate-hashes", "--no-header",
                    "-c", baseLock, "-c", installed, "-o", pluginsLock
                ],
                cancellationToken);
            if (exitCode != 0) return Failure("生成插件层锁定文件失败", output);
        }

        string current = await File.ReadAllTextAsync(pluginsLock, cancellationToken);
        if (commitBase) File.Copy(baseLock, _layout.BaseLockFile, overwrite: true);
        File.Move(pluginsLock, _layout.PluginsLockFile, overwrite: true);
        IReadOnlyList<Nb2VersionChange> changes = Changes(previous, current);
        foreach (Nb2VersionChange change in changes)
            _logger.LogInformation("NB2 插件层版本变化：{Package} {From} → {To}", change.Package, change.From ?? "（无）", change.To ?? "（无）");
        return Nb2ChangeResult.Success(changes);
    }

    private async Task<Nb2ChangeResult?> ProbeAsync(IReadOnlyList<Nb2PluginEntry> plugins, string work, CancellationToken cancellationToken)
    {
        await WriteScriptsAsync(cancellationToken);
        string store = Path.Combine(work, "probe-store");
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        int exitCode;
        string output;
        try
        {
            (exitCode, output) = await RunAsync(
                _layout.Python,
                [_layout.BotScript],
                timeout.Token,
                // 以 nb2 目录为工作目录，与正式运行一样读取 .env。
                workingDirectory: _layout.Root,
                environment: Nb2Environment.BuildProbe([.. plugins.Select(entry => entry.Module)], store));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure($"试加载超过 {ProbeTimeout.TotalMinutes:0} 分钟未结束，可能有插件在加载时卡住（如导入时访问网络）", string.Empty);
        }

        if (exitCode == 0) return null;

        string failed = output.Split('\n').FirstOrDefault(line => line.StartsWith(PROBE_MARKER, StringComparison.Ordinal))?[PROBE_MARKER.Length..]?.Trim()
                        ?? "（未输出结果）";
        return Failure($"试加载插件失败，加载失败的模块：{failed}", output);
    }

    // pins 是插件层版本约束文件（不带哈希）；为 null 时不约束插件层。
    private IReadOnlyList<string> Constraints(string baseLock, string? pins)
    {
        List<string> arguments = ["-c", baseLock];
        if (pins is not null) arguments.AddRange(["-c", pins]);
        if (File.Exists(_layout.ConstraintsFile)) arguments.AddRange(["-c", _layout.ConstraintsFile]);
        arguments.AddRange(["--prerelease", "disallow"]);
        return arguments;
    }

    private string Requirement(Nb2PluginEntry entry) =>
        entry.Wheel is { } wheel ? Path.Combine(_layout.WheelsDirectory, wheel) : $"{entry.Package}=={entry.Version}";

    private Nb2ChangeResult Failure(string summary, string output)
    {
        string message = output.Length == 0 ? summary : $"{summary}：{Tail(output)}";
        _logger.LogError("{Message}", message);
        return Nb2ChangeResult.Failed(message);
    }

    private static string? TryRead(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    private void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        // 在 finally 中调用：抛出会让已经成功的变更报告为失败，插件清单与锁定文件随之不一致。
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "删除 NB2 依赖变更的临时目录 {Directory} 失败", directory);
        }
    }

    // PEP 503：包名不区分大小写，-、_、. 视为相同。
    private static string Normalize(string name) => SeparatorPattern().Replace(name.ToLowerInvariant(), "-");

    private static IEnumerable<string> LockBody(byte[] content) =>
        Encoding.UTF8.GetString(content)
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => !line.TrimStart().StartsWith('#'));

    private static string Tail(string output) => output.Length > OUTPUT_TAIL ? output[^OUTPUT_TAIL..] : output;

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string                                 fileName,
        IReadOnlyList<string>                  arguments,
        CancellationToken                      cancellationToken,
        bool                                   includeErrors    = true,
        string?                                workingDirectory = null,
        IReadOnlyList<Nb2EnvironmentVariable>? environment      = null)
    {
        ProcessStartInfo start = new(fileName, arguments)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };
        if (workingDirectory is not null) start.WorkingDirectory = workingDirectory;
        if (environment is not null)
        {
            foreach (Nb2EnvironmentVariable variable in environment) start.Environment[variable.Name] = variable.Value;
        }

        using Process process = Process.Start(start) ?? throw new IOException($"无法启动 {fileName}");
        Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> error  = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 超时放弃时结束整个进程树，不留下仍在改写 venv 的 uv。
            process.Kill(entireProcessTree: true);
            throw;
        }

        string text = includeErrors ? (await output + await error).Trim() : await output;
        return (process.ExitCode, text);
    }

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)==(?<version>[^\s;\\]+)")]
    private static partial Regex PinnedPattern();

    [GeneratedRegex(@"\buv (?<version>\d+\.\d+\.\d+)")]
    private static partial Regex UvVersionPattern();

    [GeneratedRegex("[-_.]+")]
    private static partial Regex SeparatorPattern();
}

/// <summary>一次依赖变更的结果。</summary>
public sealed class Nb2ChangeResult
{
    /// <summary>失败原因；成功为 null。失败时环境已还原。</summary>
    public string? Error { get; init; }

    /// <summary>失败是否因为与已装插件锁定的版本冲突，可由管理员确认后放宽重试。</summary>
    public bool Conflict { get; init; }

    /// <summary>插件层的版本变化（成功时）。</summary>
    public IReadOnlyList<Nb2VersionChange> Changes { get; init; } = [];

    /// <summary>成功。</summary>
    public static Nb2ChangeResult Success(IReadOnlyList<Nb2VersionChange> changes) => new() { Changes = changes };

    /// <summary>失败。</summary>
    public static Nb2ChangeResult Failed(string error) => new() { Error = error };
}

/// <summary>插件层一个包的版本变化。</summary>
public sealed class Nb2VersionChange
{
    /// <summary>包名（PEP 503 规范化）。</summary>
    public required string Package { get; init; }

    /// <summary>变更前的版本；新增的包为 null。</summary>
    public string? From { get; init; }

    /// <summary>变更后的版本；移除的包为 null。</summary>
    public string? To { get; init; }
}
