namespace Shinano.Plugin.Bilibili.Models;

/// <summary>扫码登录的二维码。</summary>
public sealed class BiliQrCode
{
    /// <summary>轮询扫码结果用的键。</summary>
    public required string Key { get; init; }

    /// <summary>二维码 PNG。</summary>
    public required byte[] Png { get; init; }
}
