namespace Shinano.Infrastructure.Logging;

/// <summary>日志消息片段的种类：模板中的普通文字，或按值类型区分的参数。</summary>
public enum LogSegmentKind
{
    /// <summary>消息模板中的普通文字。</summary>
    Text,

    /// <summary>字符串参数。</summary>
    String,

    /// <summary>数值参数。</summary>
    Number,

    /// <summary>布尔参数。</summary>
    Boolean,

    /// <summary>空值参数。</summary>
    Null,

    /// <summary>其他参数（枚举、时间、集合、结构等）。</summary>
    Other
}
