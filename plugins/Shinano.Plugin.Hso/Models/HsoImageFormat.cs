namespace Shinano.Plugin.Hso.Models;

/// <summary>图库接受的一种图片格式。</summary>
public sealed class HsoImageFormat
{
    /// <summary>MIME 类型。</summary>
    public required string MediaType { get; init; }

    /// <summary>对象 key 的扩展名，含点号。</summary>
    public required string Extension { get; init; }
}
