namespace Shinano.Plugin.Hso.Models;

/// <summary>处理一张待审核图片的结果与附加信息。</summary>
public sealed class HsoReviewResult
{
    /// <summary>结果。</summary>
    public required HsoReviewOutcome Outcome { get; init; }

    /// <summary><see cref="HsoReviewOutcome.Duplicate" /> 时与之相同的图片 ID。</summary>
    public long? DuplicateId { get; init; }

    /// <summary>无附加信息的结果。</summary>
    public static HsoReviewResult Of(HsoReviewOutcome outcome) => new() { Outcome = outcome };
}
