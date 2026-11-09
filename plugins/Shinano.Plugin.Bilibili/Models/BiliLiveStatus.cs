namespace Shinano.Plugin.Bilibili.Models;

/// <summary>一个 UP 的直播状态。</summary>
public sealed class BiliLiveStatus
{
    /// <summary>UP 的 UID。</summary>
    public required long Uid { get; init; }

    /// <summary>直播间号。</summary>
    public required long RoomId { get; init; }

    /// <summary>是否正在直播（<c>live_status</c> 为 1）；轮播与未开播都为 false。</summary>
    public required bool Live { get; init; }

    /// <summary>本次开播时间；未开播或接口没有给出时为 null。</summary>
    public DateTimeOffset? LiveSince { get; init; }
}
