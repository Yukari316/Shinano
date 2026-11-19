namespace Shinano.Host.Panel.HostConfig;

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

/// <summary>宿主配置项的值类型。</summary>
public enum HostConfigFieldKind
{
    /// <summary>字符串。</summary>
    Text,

    /// <summary>整数，可限定范围。</summary>
    Integer,

    /// <summary>整数列表，如 QQ 号列表。</summary>
    IntegerList,

    /// <summary>从固定选项中选择的字符串。</summary>
    Choice,

    /// <summary>开关。</summary>
    Boolean,

    /// <summary>敏感项：只写，读取时只显示是否已配置，面板不回显其值。</summary>
    Secret
}

/// <summary>保存宿主配置的结果。</summary>
public sealed class HostConfigSaveResult
{
    /// <summary>校验或连接检查的错误；不为空时没有写入任何修改。</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>检查通过时的补充信息，如 LLBot 连上的账号。</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
