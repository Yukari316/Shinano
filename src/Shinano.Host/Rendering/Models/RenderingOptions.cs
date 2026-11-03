namespace Shinano.Host.Rendering.Models;

/// <summary>消息图片的部署配置，位于 <c>Shinano:Rendering</c>，重启生效。</summary>
public sealed class RenderingOptions
{
    /// <summary>配置节名。</summary>
    public const string SECTION_NAME = "Shinano:Rendering";

    /// <summary>输出宽度的下限，单位像素。</summary>
    public const int MIN_WIDTH = 480;

    /// <summary>输出宽度的上限，单位像素。</summary>
    public const int MAX_WIDTH = 3600;

    /// <summary>输出宽度，单位像素；版面按 720 宽排版后等比缩放，高度随内容增长。</summary>
    public int Width { get; set; } = 1200;
}
