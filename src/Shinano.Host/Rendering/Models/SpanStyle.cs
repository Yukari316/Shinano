namespace Shinano.Host.Rendering.Models;

/// <summary>文档行内文字的样式，可以组合。</summary>
[Flags]
internal enum SpanStyle
{
    /// <summary>正文。</summary>
    None = 0,

    /// <summary>粗体。</summary>
    Strong = 1,

    /// <summary>强调：信号黄荧光笔底（没有斜体字重）。</summary>
    Emphasis = 2,

    /// <summary>删除线。</summary>
    Strike = 4,

    /// <summary>行内代码：等宽字体、凹陷底色。</summary>
    Code = 8,

    /// <summary>链接：信号墨色下划线。</summary>
    Link = 16,

    /// <summary>次要文字，如附上的链接地址与图片说明。</summary>
    Secondary = 32,

    /// <summary>单独发送的图片在原位的「图 n」占位：信号黄底。</summary>
    Placeholder = 64
}
