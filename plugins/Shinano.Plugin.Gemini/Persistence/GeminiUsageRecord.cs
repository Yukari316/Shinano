using SqlSugar;

namespace Shinano.Plugin.Gemini.Persistence;

/// <summary>按部署时区日期与模型汇总的 Gemini 用量记录，位于全局 schema。</summary>
[SugarTable("gemini_usage_daily")]
public sealed class GeminiUsageRecord
{
    /// <summary>统计日期（<c>yyyy-MM-dd</c>，按部署时区计算）。</summary>
    [SugarColumn(ColumnName = "stat_date", Length = 10, IsPrimaryKey = true)]
    public string StatDate { get; set; } = string.Empty;

    /// <summary>模型 ID（不含 <c>models/</c> 前缀）。</summary>
    [SugarColumn(ColumnName = "model", Length = 128, IsPrimaryKey = true)]
    public string Model { get; set; } = string.Empty;

    /// <summary>总请求数（含成功与失败）。</summary>
    [SugarColumn(ColumnName = "request_count")]
    public long RequestCount { get; set; }

    /// <summary>成功返回有效回答的请求数。</summary>
    [SugarColumn(ColumnName = "success_count")]
    public long SuccessCount { get; set; }

    /// <summary>总输入 token 数（含缓存命中部分）。</summary>
    [SugarColumn(ColumnName = "prompt_tokens")]
    public long PromptTokens { get; set; }

    /// <summary>缓存命中的输入 token 数。</summary>
    [SugarColumn(ColumnName = "cached_tokens")]
    public long CachedTokens { get; set; }

    /// <summary>模型可见回答输出 token 数。</summary>
    [SugarColumn(ColumnName = "candidates_tokens")]
    public long CandidatesTokens { get; set; }

    /// <summary>模型思考过程消耗 token 数。</summary>
    [SugarColumn(ColumnName = "thoughts_tokens")]
    public long ThoughtsTokens { get; set; }

    /// <summary>总计 token 数。</summary>
    [SugarColumn(ColumnName = "total_tokens")]
    public long TotalTokens { get; set; }

    /// <summary>累计估算成本（USD）。</summary>
    [SugarColumn(ColumnName = "cost_usd", DecimalDigits = 6, Length = 18)]
    public decimal CostUsd { get; set; }
}
