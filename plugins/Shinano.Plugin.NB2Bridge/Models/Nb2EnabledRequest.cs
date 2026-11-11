namespace Shinano.Plugin.NB2Bridge.Models;

/// <summary>设置 NB2 插件是否响应事件的请求。</summary>
public sealed class Nb2EnabledRequest
{
    /// <summary>是否启用。</summary>
    public bool Enabled { get; init; }
}
