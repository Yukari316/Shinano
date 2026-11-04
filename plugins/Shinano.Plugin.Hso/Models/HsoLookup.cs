namespace Shinano.Plugin.Hso.Models;

/// <summary>作品信息补全的结果。</summary>
public sealed class HsoLookup
{
    /// <summary>结果类别。</summary>
    public required HsoLookupStatus Status { get; init; }

    /// <summary>查到的作品信息；<see cref="Status" /> 不是 <see cref="HsoLookupStatus.Found" /> 时为 null。</summary>
    public HsoWorkInfo? Info { get; init; }
}
