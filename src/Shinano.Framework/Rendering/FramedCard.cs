using SkiaSharp;

namespace Shinano.Framework.Rendering;

/// <summary>
/// 框架卡片：宿主绘制外框（标题块、斜楔、带取景角的面板与页脚），面板内容由调用方绘制，如 B站动态卡片。
/// </summary>
public sealed class FramedCard
{
    /// <summary>标题块左上的类型标签，只用英文字母、数字与常见符号，如 <c>BILIBILI · VIDEO</c>。</summary>
    public required string Label { get; init; }

    /// <summary>标题块右上的说明，如发布时间；没有时为 null。</summary>
    public string? Meta { get; init; }

    /// <summary>标题。</summary>
    public required string Title { get; init; }

    /// <summary>副标题；没有时为 null。</summary>
    public string? Subtitle { get; init; }

    /// <summary>标题左侧的圆形头像（任意 SkiaSharp 可解码的图片字节）；为 null 或无法解码时不画。</summary>
    public byte[]? Avatar { get; init; }

    /// <summary>
    /// 面板内容的绘制：画布原点为内容区左上角，参数为画布与内容宽度（按 720 宽的逻辑版面，单位与宿主卡片相同），
    /// 返回内容高度；内容的上下留白由调用方自行包含在高度内。只在绘制期间调用一次。
    /// </summary>
    public required Func<SKCanvas, float, float> DrawContent { get; init; }

    /// <summary>页脚左侧的文字；为 null 时不显示。</summary>
    public string? FooterLeft { get; init; }

    /// <summary>页脚右侧的文字；为 null 时不显示。</summary>
    public string? FooterRight { get; init; }
}
