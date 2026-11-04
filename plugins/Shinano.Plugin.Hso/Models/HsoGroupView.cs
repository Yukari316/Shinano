namespace Shinano.Plugin.Hso.Models;

/// <summary>面板中一个群的设置。</summary>
public sealed class HsoGroupView
{
    /// <summary>群号。</summary>
    public required long GroupId { get; init; }

    /// <summary>群名；Bot 已不在该群或取不到时为 null。</summary>
    public string? Name { get; init; }

    /// <summary>是否屏蔽。</summary>
    public required bool Blocked { get; init; }

    /// <summary>是否允许 NSFW。</summary>
    public required bool AllowNsfw { get; init; }
}
