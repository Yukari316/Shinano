namespace Shinano.Host.Rendering.Models;

/// <summary>一段同样式的行内文字；文字中的 <c>\n</c> 为强制换行。</summary>
internal sealed class RichSpan
{
    /// <summary>文字。</summary>
    public required string Text { get; init; }

    /// <summary>样式。</summary>
    public SpanStyle Style { get; init; }
}
