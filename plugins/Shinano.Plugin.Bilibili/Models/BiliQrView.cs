namespace Shinano.Plugin.Bilibili.Models;

/// <summary>面板显示的二维码。</summary>
public sealed class BiliQrView
{
    /// <summary>轮询扫码结果用的键。</summary>
    public required string Key { get; init; }

    /// <summary>二维码 PNG 的 data URL。</summary>
    public required string Image { get; init; }
}
