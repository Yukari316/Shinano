namespace Shinano.Plugin.Hso.Models;

/// <summary>面板修改群设置的请求。</summary>
public sealed class HsoGroupUpdate
{
    /// <summary>是否屏蔽。</summary>
    public bool Blocked { get; init; }

    /// <summary>是否允许 NSFW。</summary>
    public bool AllowNsfw { get; init; }
}
