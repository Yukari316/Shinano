namespace Shinano.Plugin.Bilibili.Models;

/// <summary>面板可选的推送群。</summary>
public sealed class BiliGroupView
{
    /// <summary>群号。</summary>
    public required long Id { get; init; }

    /// <summary>群名；Bot 已不在该群时为 null。</summary>
    public string? Name { get; init; }
}
