namespace Shinano.Plugin.Bilibili.Models;

/// <summary>已登录的 B站账号。</summary>
public sealed class BiliAccount
{
    /// <summary>UID。</summary>
    public required long Uid { get; init; }

    /// <summary>昵称。</summary>
    public required string Name { get; init; }
}
