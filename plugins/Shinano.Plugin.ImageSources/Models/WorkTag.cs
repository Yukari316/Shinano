namespace Shinano.Plugin.ImageSources.Models;

/// <summary>作品的一个标签。</summary>
public sealed class WorkTag
{
    /// <summary>标签原值。</summary>
    public required string Name { get; init; }

    /// <summary>Pixiv 的中文翻译（图源服务以 zh-CN 请求 Pixiv）；没有翻译或来源不是 Pixiv 时为 null。</summary>
    public string? Translation { get; init; }
}
