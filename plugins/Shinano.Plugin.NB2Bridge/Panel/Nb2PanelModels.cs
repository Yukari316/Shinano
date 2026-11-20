using System.Text.Json.Nodes;
using Shinano.Plugin.NB2Bridge.PluginPackages;
using Shinano.Plugin.NB2Bridge.Runtime;

namespace Shinano.Plugin.NB2Bridge.Panel;

/// <summary>NB2 页面插件列表的一项：清单中的安装信息，NB2 运行时加上进程内的实时状态。</summary>
public sealed class Nb2PluginView
{
    /// <summary>PyPI 包名。</summary>
    public required string Package { get; init; }

    /// <summary>模块名。</summary>
    public required string Module { get; init; }

    /// <summary>安装的版本。</summary>
    public required string Version { get; init; }

    /// <summary>是否经上传的 wheel 安装。</summary>
    public required bool FromWheel { get; init; }

    /// <summary>是否启用事件响应。</summary>
    public required bool Enabled { get; init; }

    /// <summary>安装时间。</summary>
    public required DateTimeOffset InstalledAt { get; init; }

    /// <summary>NB2 进程内的状态；NB2 未运行或读取失败时为 null。</summary>
    public Nb2RemoteView? Remote { get; init; }
}

/// <summary>NB2 插件配置表单：配置模型的 JSON Schema 与当前生效值。</summary>
public sealed class Nb2ConfigView
{
    /// <summary>pydantic 生成的 JSON Schema。</summary>
    public required JsonObject Schema { get; init; }

    /// <summary>当前生效值（含 <c>.env</c> 与默认值）。</summary>
    public required JsonObject Values { get; init; }
}

/// <summary>设置 NB2 插件是否响应事件的请求。</summary>
public sealed class Nb2EnabledRequest
{
    /// <summary>是否启用。</summary>
    public bool Enabled { get; init; }
}

/// <summary>从 PyPI 安装 NB2 插件的请求。</summary>
public sealed class Nb2InstallRequest
{
    /// <summary>PyPI 包名。</summary>
    public string Package { get; init; } = string.Empty;

    /// <summary>版本。</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>顶层模块名；为空时取包名把 <c>-</c> 换成 <c>_</c>。</summary>
    public string? Module { get; init; }

    /// <summary>允许调整已装插件的依赖：与已装插件冲突后由管理员确认重试时为 true。</summary>
    public bool RelaxInstalled { get; init; }
}

/// <summary>NB2 进程内插件状态的页面视图（不含配置值）。</summary>
public sealed class Nb2RemoteView
{
    /// <summary>是否加载成功。</summary>
    public required bool Loaded { get; init; }

    /// <summary>显示名称。</summary>
    public required string Name { get; init; }

    /// <summary>描述。</summary>
    public required string Description { get; init; }

    /// <summary>matcher 数。</summary>
    public required int Matchers { get; init; }

    /// <summary>是否声明了配置模型。</summary>
    public required bool HasConfig { get; init; }

    /// <summary>统计。</summary>
    public required Nb2PluginStats Stats { get; init; }
}

/// <summary>依赖变更（安装、卸载、重建）成功的结果。</summary>
public sealed class Nb2ChangeView
{
    /// <summary>插件层的版本变化，按包名排列。</summary>
    public required IReadOnlyList<Nb2VersionChange> Changes { get; init; }
}

/// <summary>管理员约束文件的内容；读取与保存共用。</summary>
public sealed class Nb2ConstraintsView
{
    /// <summary>约束文本（requirements 格式，每行一个包名与版本条件，可带 <c># 注释</c>）。</summary>
    public string Text { get; init; } = string.Empty;
}
