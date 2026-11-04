namespace Shinano.Plugin.Hso.Models;

/// <summary>面板设置图片 NSFW 的请求。</summary>
public sealed class HsoNsfwUpdate
{
    /// <summary>是否为 NSFW。</summary>
    public bool Nsfw { get; init; }
}
