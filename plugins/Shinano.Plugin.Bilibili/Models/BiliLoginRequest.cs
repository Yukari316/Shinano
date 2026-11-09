namespace Shinano.Plugin.Bilibili.Models;

/// <summary>面板发起扫码登录。</summary>
public sealed class BiliLoginRequest
{
    /// <summary>已有登录时是否确认退出原账号；为 false 且已登录时不生成二维码，返回原账号供确认。</summary>
    public bool ConfirmLogout { get; init; }
}
