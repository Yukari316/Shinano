namespace Shinano.Host.Panel.Models;

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
