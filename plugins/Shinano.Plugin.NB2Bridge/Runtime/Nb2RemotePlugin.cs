using System.Text.Json.Nodes;

namespace Shinano.Plugin.NB2Bridge.Runtime;

/// <summary>控制插件 <c>GET /shinano/plugins</c> 返回的一项：NB2 进程内该插件的实时状态。</summary>
public sealed class Nb2RemotePlugin
{
    /// <summary>模块名。</summary>
    public required string Module { get; init; }

    /// <summary>是否加载成功。</summary>
    public required bool Loaded { get; init; }

    /// <summary>NB2 内是否响应事件。</summary>
    public required bool Enabled { get; init; }

    /// <summary><c>PluginMetadata.name</c>；没有元数据时为模块名。</summary>
    public required string Name { get; init; }

    /// <summary><c>PluginMetadata.description</c>。</summary>
    public required string Description { get; init; }

    /// <summary>当前登记的 matcher 数（含临时 matcher）。</summary>
    public required int Matchers { get; init; }

    /// <summary><c>PluginMetadata.config</c> 的 JSON Schema；未声明配置模型时为 null。</summary>
    public JsonObject? ConfigSchema { get; init; }

    /// <summary>配置的当前生效值；未声明配置模型时为 null。</summary>
    public JsonObject? Config { get; init; }

    /// <summary>进程启动以来的统计。</summary>
    public required Nb2PluginStats Stats { get; init; }
}

/// <summary>NB2 控制插件 <c>/shinano/status</c> 返回的运行环境版本（只取用到的字段）。</summary>
public sealed class Nb2RemoteStatus
{
    /// <summary>nonebot2 包版本，如 <c>2.5.0</c>。</summary>
    public required string Nonebot { get; init; }

    /// <summary>Python 版本，如 <c>3.12.11</c>。</summary>
    public required string Python { get; init; }
}

/// <summary>NB2 进程内一个插件的统计，进程重启后清零。</summary>
public sealed class Nb2PluginStats
{
    /// <summary>到达该插件 matcher 的事件数（被禁用而忽略的不计）。</summary>
    public required long Events { get; init; }

    /// <summary>matcher 实际运行次数。</summary>
    public required long Runs { get; init; }

    /// <summary>运行出错次数。</summary>
    public required long Errors { get; init; }

    /// <summary>经 matcher 发起的平台 API 调用数。</summary>
    public required long ApiCalls { get; init; }

    /// <summary>最近一次错误；没有时为 null。</summary>
    public Nb2PluginError? LastError { get; init; }
}

/// <summary>NB2 插件 matcher 运行时的一次错误。</summary>
public sealed class Nb2PluginError
{
    /// <summary>异常类型名。</summary>
    public required string Type { get; init; }

    /// <summary>异常消息。</summary>
    public required string Message { get; init; }

    /// <summary>发生时间（Unix 秒）。</summary>
    public required long Time { get; init; }
}
