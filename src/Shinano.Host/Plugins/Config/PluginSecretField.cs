namespace Shinano.Host.Plugins.Config;

/// <summary>插件机密表单中的一项；不含机密的值。</summary>
public sealed class PluginSecretField
{
    /// <summary>机密类型中的属性名，保存时以此为键。</summary>
    public required string Key { get; init; }

    /// <summary>显示名称。</summary>
    public required string Label { get; init; }

    /// <summary>说明；没有时为空。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>是否可在面板写入；只有字符串属性可写，其余只显示状态。</summary>
    public required bool Editable { get; init; }

    /// <summary>是否已配置。</summary>
    public required bool Configured { get; init; }
}
