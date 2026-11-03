namespace Shinano.Framework.Rendering.Models;

/// <summary>文档中图片的处理方式。</summary>
public enum DocumentImageMode
{
    /// <summary>按正文宽度等比缩小（不放大）画进文档图，说明写在图下。</summary>
    Inline,

    /// <summary>原位画「图 n」占位，图片另行返回，由调用方随文档图之后发送。</summary>
    Separate
}
