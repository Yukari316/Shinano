using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Shinano.Plugin.NB2Bridge.Runtime;

namespace Shinano.Plugin.NB2Bridge.PluginPackages;

/// <summary>
/// NB2 插件包的安装、卸载与环境重建：停止 NB2 后由 <see cref="Nb2Venv" /> 执行依赖变更（约束、试加载、锁定与失败还原），
/// 成功才更新插件清单，完成后恢复 NB2 原来的运行状态。环境需要重建期间（基础层与当前 NB2Bridge 期望不一致）不允许安装或升级，
/// 卸载照常。变更一旦开始就不随请求取消（管理员关闭页面），只受固定超时限制。
/// </summary>
public sealed partial class Nb2Packages
{
    private readonly Nb2Venv              _venv;
    private readonly Nb2Catalog           _catalog;
    private readonly Nb2Runner            _runner;
    private readonly Nb2Layout            _layout;
    private readonly TimeProvider         _time;
    private readonly ILogger<Nb2Packages> _logger;

    /// <summary>创建包管理。</summary>
    public Nb2Packages(Nb2Venv venv, Nb2Catalog catalog, Nb2Runner runner, Nb2Layout layout, TimeProvider time, ILogger<Nb2Packages> logger)
    {
        _venv    = venv;
        _catalog = catalog;
        _runner  = runner;
        _layout  = layout;
        _time    = time;
        _logger  = logger;
    }

    /// <summary>从 PyPI 安装或升级插件。</summary>
    /// <param name="package">PyPI 包名。</param>
    /// <param name="version">版本。</param>
    /// <param name="module">顶层模块名；为空时取包名把 <c>-</c> 换成 <c>_</c>。</param>
    /// <param name="relaxInstalled">允许调整已装插件的依赖（不以插件层锁定文件为约束）。</param>
    /// <param name="cancellationToken">只用于等待前一个变更结束。</param>
    public Task<Nb2ChangeResult> InstallAsync(string package, string version, string? module, bool relaxInstalled, CancellationToken cancellationToken)
    {
        if (!PackagePattern().IsMatch(package)) return Task.FromResult(Nb2ChangeResult.Failed("包名只能包含字母、数字、.、_ 与 -"));
        if (!VersionPattern().IsMatch(version)) return Task.FromResult(Nb2ChangeResult.Failed("版本号格式不正确"));
        string moduleName = string.IsNullOrWhiteSpace(module) ? DefaultModule(package) : module.Trim();
        if (!ModulePattern().IsMatch(moduleName)) return Task.FromResult(Nb2ChangeResult.Failed("模块名必须是 Python 标识符"));

        return _runner.ExclusiveAsync(
            _ => InstallCoreAsync($"{package}=={version}", package, version, moduleName, null, relaxInstalled),
            cancellationToken);
    }

    /// <summary>上传 wheel 安装或升级插件。</summary>
    /// <param name="fileName">wheel 文件名（含包名与版本）。</param>
    /// <param name="content">文件内容。</param>
    /// <param name="module">顶层模块名；为空时取包名。</param>
    /// <param name="relaxInstalled">允许调整已装插件的依赖（不以插件层锁定文件为约束）。</param>
    /// <param name="cancellationToken">用于读取上传内容与等待前一个变更结束。</param>
    public async Task<Nb2ChangeResult> InstallWheelAsync(string fileName, Stream content, string? module, bool relaxInstalled, CancellationToken cancellationToken)
    {
        string name = Path.GetFileName(fileName);
        Match match = WheelPattern().Match(name);
        if (!match.Success) return Nb2ChangeResult.Failed("文件名不是有效的 wheel（<包名>-<版本>-<标签>.whl）");

        string package = match.Groups["name"].Value.Replace('_', '-');
        string version = match.Groups["version"].Value;
        string moduleName = string.IsNullOrWhiteSpace(module) ? DefaultModule(package) : module.Trim();
        if (!ModulePattern().IsMatch(moduleName)) return Nb2ChangeResult.Failed("模块名必须是 Python 标识符");

        Directory.CreateDirectory(_layout.WheelsDirectory);
        string path = Path.Combine(_layout.WheelsDirectory, name);
        string temporary = path + ".upload";
        await using (FileStream file = File.Create(temporary))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return await _runner.ExclusiveAsync(
            async _ =>
            {
                // 同名即同一版本：插件层锁定文件按路径与哈希记录已装的 wheel，覆盖后安装失败时无法按锁定文件还原。
                // 只看插件清单：不在清单中的同名文件是中途退出留下的残留，直接覆盖。
                if (_catalog.Entries().Any(item => string.Equals(item.Wheel, name, StringComparison.OrdinalIgnoreCase)))
                {
                    File.Delete(temporary);
                    return Nb2ChangeResult.Failed($"已安装同名的 wheel {name}；要替换请先卸载该插件，或提高版本号后重新打包");
                }

                File.Move(temporary, path, overwrite: true);
                Nb2ChangeResult result = await InstallCoreAsync(path, package, version, moduleName, name, relaxInstalled);
                if (result.Error is not null) File.Delete(path);
                return result;
            },
            cancellationToken);
    }

    /// <summary>卸载插件。</summary>
    public Task<Nb2ChangeResult> UninstallAsync(string module, CancellationToken cancellationToken)
    {
        if (_catalog.Find(module) is not { } entry) return Task.FromResult(Nb2ChangeResult.Failed($"没有安装模块为 {module} 的插件"));

        return _runner.ExclusiveAsync(
            async _ =>
            {
                _logger.LogInformation("卸载 NB2 插件 {Package}（{Module}）", entry.Package, entry.Module);
                Nb2ChangeResult result = await _venv.UninstallAsync(entry.Package, [.. _catalog.Entries().Where(item => item.Module != module)]);
                if (result.Error is not null) return result;

                _catalog.Remove(module);
                if (entry.Wheel is { } wheel) File.Delete(Path.Combine(_layout.WheelsDirectory, wheel));
                _logger.LogInformation("已卸载 NB2 插件 {Package}", entry.Package);
                return result;
            },
            cancellationToken);
    }

    /// <summary>按当前 NB2Bridge 期望的基础锁定文件重建环境（见 <see cref="Nb2Venv.RebuildAsync" />）。</summary>
    public Task<Nb2ChangeResult> RebuildAsync(CancellationToken cancellationToken) =>
        _runner.ExclusiveAsync(
            async _ =>
            {
                _logger.LogInformation("面板请求重建 NB2 环境");
                Nb2ChangeResult result = await _venv.RebuildAsync(_catalog.Entries());
                if (result.Error is null) _logger.LogInformation("NB2 环境重建完成，插件层 {Count} 个包版本变化", result.Changes.Count);
                return result;
            },
            cancellationToken);

    private static string DefaultModule(string package) => package.Replace('-', '_').Replace('.', '_').ToLowerInvariant();

    private async Task<Nb2ChangeResult> InstallCoreAsync(
        string  requirement,
        string  package,
        string  version,
        string  module,
        string? wheel,
        bool    relaxInstalled)
    {
        Nb2BaseState state = _venv.State();
        if (state != Nb2BaseState.Current) return Nb2ChangeResult.Failed(StateError(state));
        Nb2PluginEntry? existing = _catalog.Find(module);
        if (existing is not null && !string.Equals(existing.Package, package, StringComparison.OrdinalIgnoreCase))
            return Nb2ChangeResult.Failed($"模块 {module} 已属于插件 {existing.Package}");

        _logger.LogInformation(
            "安装 NB2 插件 {Requirement}（模块 {Module}{Relax}）",
            requirement,
            module,
            relaxInstalled ? "，允许调整已装插件的依赖" : string.Empty);
        Nb2PluginEntry entry = new()
        {
            Package     = package,
            Module      = module,
            Version     = version,
            Wheel       = wheel,
            Enabled     = existing?.Enabled ?? false,
            InstalledAt = _time.GetUtcNow()
        };
        Nb2ChangeResult result = await _venv.InstallAsync(package, requirement, [.. _catalog.Entries().Where(item => item.Module != module), entry], relaxInstalled);
        if (result.Error is not null) return result;

        _catalog.Put(entry);
        // 升级后插件层锁定文件只引用新的来源，旧 wheel 不再需要；留着会挡住以后回滚到该版本的上传。
        if (existing?.Wheel is { } oldWheel && !string.Equals(oldWheel, wheel, StringComparison.OrdinalIgnoreCase))
            File.Delete(Path.Combine(_layout.WheelsDirectory, oldWheel));
        _logger.LogInformation("已安装 NB2 插件 {Package} {Version}", package, version);
        return result;
    }

    private static string StateError(Nb2BaseState state) =>
        state switch
        {
            Nb2BaseState.Missing => "未找到 NB2 运行环境，请在部署机运行 scripts 下的 nb2-setup",
            _                    => "NB2 基础环境需要重建，请先在本页重建环境再安装插件"
        };

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
