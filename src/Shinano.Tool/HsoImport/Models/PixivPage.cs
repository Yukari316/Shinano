namespace Shinano.Tool.HsoImport.Models;

/// <summary>从文件名解析出的 Pixiv 作品号与页号。</summary>
public sealed class PixivPage
{
    /// <summary>作品号。</summary>
    public required long Pid { get; init; }

    /// <summary>页号，从 0 开始。</summary>
    public required int Page { get; init; }
}
