namespace Shinano.Plugin.NB2Bridge.Models;

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
