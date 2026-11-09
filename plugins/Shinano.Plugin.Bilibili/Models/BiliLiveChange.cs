namespace Shinano.Plugin.Bilibili.Models;

/// <summary>直播状态的变化。</summary>
public enum BiliLiveChange
{
    /// <summary>没有需要提醒的变化。</summary>
    None,

    /// <summary>开播。</summary>
    Started,

    /// <summary>下播。</summary>
    Ended
}
