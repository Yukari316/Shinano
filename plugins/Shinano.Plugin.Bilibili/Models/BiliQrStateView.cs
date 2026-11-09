namespace Shinano.Plugin.Bilibili.Models;

/// <summary>面板轮询的扫码进度。</summary>
public sealed class BiliQrStateView
{
    /// <summary>进度。</summary>
    public required BiliQrState State { get; init; }

    /// <summary>登录成功后的账号；其他状态为 null。</summary>
    public BiliAccount? Account { get; init; }
}
