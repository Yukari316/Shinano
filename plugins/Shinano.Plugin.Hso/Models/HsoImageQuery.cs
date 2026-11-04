namespace Shinano.Plugin.Hso.Models;

/// <summary>面板图库的筛选与分页。</summary>
public sealed class HsoImageQuery
{
    /// <summary>每页张数。</summary>
    public const int PAGE_SIZE = 20;

    /// <summary>页码，从 1 开始。</summary>
    public int Page { get; init; } = 1;

    /// <summary>只看 NSFW 或非 NSFW；为 null 时不筛选。</summary>
    public bool? Nsfw { get; init; }

    /// <summary>只看有或没有 pid 的；为 null 时不筛选。</summary>
    public bool? HasPid { get; init; }

    /// <summary>只看被拦截过或没有被拦截的；为 null 时不筛选。</summary>
    public bool? Blocked { get; init; }

    /// <summary>关键字：数字匹配 pid，其余匹配标题、作者与文件名；为空时不筛选。</summary>
    public string? Keyword { get; init; }
}
