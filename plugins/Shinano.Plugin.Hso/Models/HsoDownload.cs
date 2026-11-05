namespace Shinano.Plugin.Hso.Models;

/// <summary>下载原图的结果：成功时有 <see cref="Content" />，失败时有 <see cref="Error" />。</summary>
public sealed class HsoDownload
{
    /// <summary>原图内容；失败时为 null。</summary>
    public byte[]? Content { get; init; }

    /// <summary>失败原因（给管理员看）；成功时为 null。</summary>
    public string? Error { get; init; }
}
