namespace Shinano.Plugin.ImageSearch.Models;

/// <summary>综合各搜索服务结果后的回复决定：回复一条结果，或回复一句文案。</summary>
public sealed class SearchDecision
{
    /// <summary>要回复的结果；为 null 时回复 <see cref="Reply" />。</summary>
    public SearchHit? Hit { get; init; }

    /// <summary>结果所属搜索服务的相似度阈值，单位百分比，显示在卡片的相似度刻度上。</summary>
    public double Threshold { get; init; }

    /// <summary>结果的相似度低于阈值，卡片以警告色显示。</summary>
    public bool LowSimilarity { get; init; }

    /// <summary>没有结果时的回复文案。</summary>
    public string Reply { get; init; } = string.Empty;
}
