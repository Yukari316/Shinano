using Shinano.Framework.Rendering.Models;

namespace Shinano.Framework.Plugins.Capabilities;

/// <summary>宿主统一风格的图片绘制。线程安全。</summary>
public interface IPluginImages
{
    /// <summary>把文档绘制为 PNG；发送时以 <c>base64://</c> 加 Base64 内容作为图片地址，不依赖 RustFS。</summary>
    /// <returns>PNG 字节。</returns>
    /// <exception cref="InvalidOperationException">宿主字体文件缺失，无法绘制；调用方应改发文字。</exception>
    byte[] RenderTextImage(TextImageDocument document);
}
