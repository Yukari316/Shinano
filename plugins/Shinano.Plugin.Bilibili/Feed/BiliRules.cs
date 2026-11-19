using Shinano.Plugin.Bilibili.Persistence;

namespace Shinano.Plugin.Bilibili.Feed;

/// <summary>推送规则：哪些动态可推送、推送进度之后的新动态、直播状态变化。只做判断，不访问外部。</summary>
public static class BiliRules
{
    /// <summary>每个 UP 每轮最多推送的动态数。</summary>
    public const int MAX_PER_ROUND = 3;

    /// <summary>可以推送的动态：不是置顶，也不是开播产生的直播推荐。</summary>
    public static bool Pushable(BiliDynamic dynamic) => !dynamic.Pinned && dynamic.Kind != BiliDynamicKind.LiveRecommend;

    /// <summary>动态是否在推送进度之后：发布时间更晚，或同一秒发布而 ID 更大。</summary>
    public static bool After(BiliDynamic dynamic, long pushedAt, string? pushedId)
    {
        long published = dynamic.PublishedAt.ToUnixTimeSeconds();
        return published > pushedAt || published == pushedAt && CompareIds(dynamic.Id, pushedId) > 0;
    }

    /// <summary>
    /// 按发布时间先后排列的、推送进度之后的可推送动态；超过每轮上限时只保留最新的若干条，<paramref name="skipped" /> 为舍去的条数。
    /// </summary>
    public static IReadOnlyList<BiliDynamic> NewSince(IEnumerable<BiliDynamic> dynamics, long pushedAt, string? pushedId, out int skipped)
    {
        List<BiliDynamic> newer = [.. Chronological(dynamics.Where(dynamic => Pushable(dynamic) && After(dynamic, pushedAt, pushedId)))];
        skipped = Math.Max(0, newer.Count - MAX_PER_ROUND);
        return skipped == 0 ? newer : newer[skipped..];
    }

    /// <summary>最新的一条可推送动态；没有时为 null。</summary>
    public static BiliDynamic? Latest(IEnumerable<BiliDynamic> dynamics) => Chronological(dynamics.Where(Pushable)).LastOrDefault();

    /// <summary>按发布时间先后排列（同一秒按 ID）。</summary>
    public static IEnumerable<BiliDynamic> Chronological(IEnumerable<BiliDynamic> dynamics) =>
        dynamics.OrderBy(dynamic => dynamic.PublishedAt).ThenBy(dynamic => dynamic.Id, Comparer<string>.Create(CompareIds));

    /// <summary>与上次记录相比的直播状态变化；没有记录时当前在播视为开播。</summary>
    public static BiliLiveChange Change(BiliLiveRecord? previous, BiliLiveStatus current) =>
        (previous?.Live ?? false, current.Live) switch
        {
            (false, true) => BiliLiveChange.Started,
            (true, false) => BiliLiveChange.Ended,
            _             => BiliLiveChange.None
        };

    // 动态 ID 为随时间增长的十进制数字串，先比长度再按字符比较即为数值大小。
    private static int CompareIds(string? left, string? right)
    {
        if (left is null || right is null) return left is null ? right is null ? 0 : -1 : 1;
        return left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
    }
}
