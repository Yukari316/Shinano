namespace Shinano.Framework.Rendering.Models;

/// <summary>文档卡片的渲染结果。</summary>
public sealed class RenderedDocument
{
    /// <summary>文档图（PNG）。</summary>
    public required byte[] Image { get; init; }

    /// <summary>
    /// <see cref="DocumentImageMode.Separate" /> 时按「图 n」顺序返回的原始图片数据，由调用方随文档图之后发送；
    /// 内嵌时为空。
    /// </summary>
    public IReadOnlyList<byte[]> Attachments { get; init; } = [];
}
