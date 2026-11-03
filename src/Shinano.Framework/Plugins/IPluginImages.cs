using Shinano.Framework.Rendering.Models;

namespace Shinano.Framework.Plugins;

/// <summary>
/// 宿主统一风格的消息图片：白底工业编辑风（参考《明日方舟：终末地》），分为通知、列表、排行、信息卡、状态报告与文档六类。
/// 输出 PNG，宽度由部署配置决定（默认 1200 像素），高度随内容增长；发送时以 <c>base64://</c> 加 Base64 内容作为图片地址，
/// 不依赖 RustFS。线程安全。
/// </summary>
/// <remarks>各方法在宿主字体文件缺失时抛出 <see cref="InvalidOperationException" />，调用方应改发文字。</remarks>
public interface IPluginImages
{
    /// <summary>通知卡片。</summary>
    /// <returns>PNG 字节。</returns>
    byte[] RenderNotice(NoticeCard card);

    /// <summary>列表卡片（一页）。</summary>
    /// <returns>PNG 字节。</returns>
    byte[] RenderList(ListCard card);

    /// <summary>排行卡片。</summary>
    /// <returns>PNG 字节。</returns>
    byte[] RenderRanking(RankingCard card);

    /// <summary>信息卡片。</summary>
    /// <returns>PNG 字节。</returns>
    byte[] RenderInfo(InfoCard card);

    /// <summary>状态报告卡片。</summary>
    /// <returns>PNG 字节。</returns>
    byte[] RenderStatus(StatusCard card);

    /// <summary>把 Markdown 渲染为文档图；单独发送的图片随结果返回。</summary>
    RenderedDocument RenderDocument(DocumentCard card);
}
