using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Shinano.Plugin.NB2Bridge.Runtime;

namespace Shinano.Plugin.NB2Bridge.PluginPackages;

/// <summary>
/// NB2 插件包的安装与卸载：停止 NB2 后用 uv 在 venv 中执行，安装后确认模块可导入，成功则更新插件清单与依赖锁定文件；
/// 任一步失败按锁定文件还原依赖（<c>uv pip sync</c>）。完成后恢复 NB2 原来的运行状态。卸载不清理其他插件可能共用的依赖。
/// </summary>
public sealed partial class Nb2Packages
{
    private const string UV                 = "uv";
    private const int    OUTPUT_TAIL        = 800;
    private const string FIND_MODULE_SCRIPT = "import importlib.util, sys; sys.exit(0 if importlib.util.find_spec(sys.argv[1]) else 1)";

    private readonly Nb2Layout            _layout;
    private readonly Nb2Catalog           _catalog;
    private readonly Nb2Runner            _runner;
    private readonly TimeProvider         _time;
    private readonly ILogger<Nb2Packages> _logger;

    /// <summary>创建包管理。</summary>
    public Nb2Packages(Nb2Layout layout, Nb2Catalog catalog, Nb2Runner runner, TimeProvider time, ILogger<Nb2Packages> logger)
    {
        _layout  = layout;
        _catalog = catalog;
        _runner  = runner;
        _time    = time;
        _logger  = logger;
    }

    /// <summary>从 PyPI 安装或升级插件。</summary>
    /// <param name="package">PyPI 包名。</param>
    /// <param name="version">版本。</param>
    /// <param name="module">顶层模块名；为空时取包名把 <c>-</c> 换成 <c>_</c>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>失败原因；成功为 null。</returns>
    public Task<string?> InstallAsync(string package, string version, string? module, CancellationToken cancellationToken)
    {
        if (!PackagePattern().IsMatch(package)) return Task.FromResult<string?>("包名只能包含字母、数字、.、_ 与 -");
        if (!VersionPattern().IsMatch(version)) return Task.FromResult<string?>("版本号格式不正确");
        string moduleName = string.IsNullOrWhiteSpace(module) ? DefaultModule(package) : module.Trim();
        if (!ModulePattern().IsMatch(moduleName)) return Task.FromResult<string?>("模块名必须是 Python 标识符");

        return _runner.ExclusiveAsync(
            token => InstallCoreAsync($"{package}=={version}", package, version, moduleName, null, token),
            cancellationToken);
    }

    /// <summary>上传 wheel 安装或升级插件。</summary>
    /// <param name="fileName">wheel 文件名（含包名与版本）。</param>
    /// <param name="content">文件内容。</param>
    /// <param name="module">顶层模块名；为空时取包名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>失败原因；成功为 null。</returns>
    public async Task<string?> InstallWheelAsync(string fileName, Stream content, string? module, CancellationToken cancellationToken)
    {
        string name = Path.GetFileName(fileName);
        Match match = WheelPattern().Match(name);
        if (!match.Success) return "文件名不是有效的 wheel（<包名>-<版本>-<标签>.whl）";

        string package = match.Groups["name"].Value.Replace('_', '-');
        string version = match.Groups["version"].Value;
        string moduleName = string.IsNullOrWhiteSpace(module) ? DefaultModule(package) : module.Trim();
        if (!ModulePattern().IsMatch(moduleName)) return "模块名必须是 Python 标识符";

        Directory.CreateDirectory(_layout.WheelsDirectory);
        string path = Path.Combine(_layout.WheelsDirectory, name);
        string temporary = path + ".upload";
        await using (FileStream file = File.Create(temporary))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return await _runner.ExclusiveAsync(
            async token =>
            {
                File.Move(temporary, path, overwrite: true);
                string? error = await InstallCoreAsync(path, package, version, moduleName, name, token);
                if (error is not null) File.Delete(path);
                return error;
            },
            cancellationToken);
    }

    /// <summary>卸载插件。</summary>
    /// <returns>失败原因；成功为 null。</returns>
    public Task<string?> UninstallAsync(string module, CancellationToken cancellationToken)
    {
        if (_catalog.Find(module) is not { } entry) return Task.FromResult<string?>($"没有安装模块为 {module} 的插件");

        return _runner.ExclusiveAsync<string?>(
            async token =>
            {
                if (EnvironmentError() is { } missing) return missing;
                _logger.LogInformation("卸载 NB2 插件 {Package}（{Module}）", entry.Package, entry.Module);
                (int exitCode, string output) = await RunAsync(UV, ["pip", "uninstall", "--python", _layout.Python, entry.Package], token);
                if (exitCode != 0)
                {
                    await RestoreAsync(token);
                    return Failed($"卸载 {entry.Package} 失败", output);
                }

                _catalog.Remove(module);
                if (entry.Wheel is { } wheel) File.Delete(Path.Combine(_layout.WheelsDirectory, wheel));
                await FreezeAsync(token);
                _logger.LogInformation("已卸载 NB2 插件 {Package}", entry.Package);
                return null;
            },
            cancellationToken);
    }

    private static string DefaultModule(string package) => package.Replace('-', '_').Replace('.', '_').ToLowerInvariant();

    private string? EnvironmentError() =>
        File.Exists(_layout.Python) ? null : "未找到 NB2 运行环境，请在部署机运行 scripts 下的 nb2-setup";

    private async Task<string?> InstallCoreAsync(
        string            requirement,
        string            package,
        string            version,
        string            module,
        string?           wheel,
        CancellationToken cancellationToken)
    {
        if (EnvironmentError() is { } missing) return missing;
        Nb2PluginEntry? existing = _catalog.Find(module);
        if (existing is not null && !string.Equals(existing.Package, package, StringComparison.OrdinalIgnoreCase))
            return $"模块 {module} 已属于插件 {existing.Package}";

        _logger.LogInformation("安装 NB2 插件 {Requirement}（模块 {Module}）", requirement, module);
        (int exitCode, string output) = await RunAsync(UV, ["pip", "install", "--python", _layout.Python, requirement], cancellationToken);
        if (exitCode != 0)
        {
            await RestoreAsync(cancellationToken);
            return Failed($"安装 {package} 失败，已还原依赖", output);
        }

        (int findExit, _) = await RunAsync(_layout.Python, ["-c", FIND_MODULE_SCRIPT, module], cancellationToken);
        if (findExit != 0)
        {
            await RestoreAsync(cancellationToken);
            _logger.LogWarning("安装 {Package} 后找不到模块 {Module}，已还原依赖", package, module);
            return $"安装后找不到模块 {module}，已还原依赖；请确认模块名";
        }

        _catalog.Put(
            new Nb2PluginEntry
            {
                Package     = package,
                Module      = module,
                Version     = version,
                Wheel       = wheel,
                Enabled     = existing?.Enabled ?? false,
                InstalledAt = _time.GetUtcNow()
            });
        await FreezeAsync(cancellationToken);
        _logger.LogInformation("已安装 NB2 插件 {Package} {Version}", package, version);
        return null;
    }

    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_layout.LockFile))
        {
            _logger.LogWarning("没有依赖锁定文件，无法还原 NB2 依赖");
            return;
        }

        (int exitCode, string output) = await RunAsync(UV, ["pip", "sync", "--python", _layout.Python, _layout.LockFile], cancellationToken);
        if (exitCode == 0) _logger.LogInformation("已按锁定文件还原 NB2 依赖");
        else _logger.LogError("按锁定文件还原 NB2 依赖失败（退出码 {ExitCode}）：{Output}", exitCode, Tail(output));
    }

    private async Task FreezeAsync(CancellationToken cancellationToken)
    {
        (int exitCode, string output) = await RunAsync(UV, ["pip", "freeze", "--python", _layout.Python], cancellationToken, includeErrors: false);
        if (exitCode != 0)
        {
            _logger.LogError("更新 NB2 依赖锁定文件失败（退出码 {ExitCode}）", exitCode);
            return;
        }

        await File.WriteAllTextAsync(_layout.LockFile, output, cancellationToken);
    }

    private string Failed(string summary, string output)
    {
        string tail = Tail(output);
        _logger.LogError("{Summary}：{Output}", summary, tail);
        return $"{summary}：{tail}";
    }

    private static string Tail(string output) => output.Length > OUTPUT_TAIL ? output[^OUTPUT_TAIL..] : output;

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string                fileName,
        IReadOnlyList<string> arguments,
        CancellationToken     cancellationToken,
        bool                  includeErrors = true)
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
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"无法启动 {fileName}");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error  = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string text = includeErrors ? (await output + await error).Trim() : await output;
        return (process.ExitCode, text);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")]
    private static partial Regex PackagePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.!+_-]{0,49}$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,99}$")]
    private static partial Regex ModulePattern();

    // PEP 427：{包名}-{版本}(-{构建号})?-{Python 标签}-{ABI 标签}-{平台标签}.whl，包名中的 - 已换成 _。
    [GeneratedRegex(@"^(?<name>[A-Za-z0-9_.]+)-(?<version>[A-Za-z0-9_.!+]+)(-\d[^-]*)?-[^-]+-[^-]+-[^-]+\.whl$")]
    private static partial Regex WheelPattern();
}
