namespace Shinano.Host.Panel.HostConfig.Models;

/// <summary>面板可显示的一个宿主配置项，位于配置节 <c>Shinano:{Section}:{Key}</c>。</summary>
public sealed class HostConfigField
{
    /// <summary>配置节名，如 <c>Deployment</c>。</summary>
    public required string Section { get; init; }

    /// <summary>配置项名。</summary>
    public required string Key { get; init; }

    /// <summary>显示名称。</summary>
    public required string Label { get; init; }

    /// <summary>说明，显示在输入框下方；没有时为空。</summary>
    public string Hint { get; init; } = string.Empty;

    /// <summary>值类型。</summary>
    public required HostConfigFieldKind Kind { get; init; }

    /// <summary>整数下限（含）。</summary>
    public long? Minimum { get; init; }

    /// <summary>整数上限（含）。</summary>
    public long? Maximum { get; init; }

    /// <summary>可选值（<see cref="HostConfigFieldKind.Choice" />）。</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>值为空时是否允许，适用于文本。</summary>
    public bool Optional { get; init; }
}
