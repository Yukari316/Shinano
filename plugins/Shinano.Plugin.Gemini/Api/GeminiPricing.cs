namespace Shinano.Plugin.Gemini.Api;

/// <summary>
/// Gemini 模型内置公开单价表（USD / 100 万 tokens），用于在日志与管理面板中估算请求成本。
/// 输出单价同时覆盖可见回答与思考 token；缓存命中输入按对应模型的隐式缓存折扣单价计费。
/// </summary>
public static class GeminiPricing
{
    private const decimal TOKENS_PER_MILLION = 1_000_000m;
    private const int     LONG_CONTEXT_LIMIT = 200_000;

    private static readonly PricingRule[] Rules =
    [
        Rule(["gemini-4-neon"], Rates(3.00m, 0.15m, 15.00m)),
        Rule(["gemini-4-hydrogen"], Rates(1.50m, 0.075m, 7.50m)),
        Rule(["gemini-4-carbon", "gemini-4-argon"], Rates(2.00m, 0.10m, 10.00m)),
        Rule(["gemini-3.8-flash"], Rates(0.75m, 0.075m, 3.75m)),
        Rule(["gemini-3.6-flash"], Rates(1.50m, 0.075m, 7.50m)),
        Rule(
            ["gemini-3.1-pro", "gemini-3-pro"],
            Rates(2.00m, 0.20m, 12.00m),
            Rates(4.00m, 0.40m, 18.00m)),
        Rule(["gemini-3-flash", "gemini-3."], Rates(0.50m, 0.05m, 3.00m)),
        Rule(
            ["gemini-2.5-pro"],
            Rates(1.25m, 0.125m, 10.00m),
            Rates(2.50m, 0.25m, 15.00m)),
        Rule(["gemini-2.5-flash-lite"], Rates(0.10m, 0.01m, 0.40m)),
        Rule(["gemini-2.5-flash"], Rates(0.30m, 0.03m, 2.50m)),
        Rule(["gemini-2.0-flash-lite"], Rates(0.075m, 0.0075m, 0.30m)),
        Rule(["gemini-2.0-flash"], Rates(0.10m, 0.025m, 0.40m))
    ];

    /// <summary>按模型与本次 token 用量估算成本（USD）；未知模型或缺少用量时返回 0。</summary>
    public static decimal EstimateCostUsd(string modelId, GeminiUsageMetadata? usage)
    {
        if (usage is null) return 0m;
        int promptTokens = Math.Max(0, usage.PromptTokens ?? 0);
        if (TryGetRates(modelId, promptTokens, out ModelRates rates) is false)
            return 0m;

        int cachedTokens = Math.Clamp(usage.CachedTokens ?? 0, 0, promptTokens);
        int uncachedInputTokens = promptTokens - cachedTokens;
        int outputTokens = Math.Max(0, usage.OutputTokens);

        decimal cost = (uncachedInputTokens * rates.InputPerMillion
                        + cachedTokens * rates.CachedInputPerMillion
                        + outputTokens * rates.OutputPerMillion)
                       / TOKENS_PER_MILLION;
        return Math.Round(cost, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>按最长前缀匹配查找模型对应的百万 token 单价（USD）。</summary>
    internal static bool TryGetRates(string modelId, int promptTokens, out ModelRates rates)
    {
        string id = GeminiModelCapabilities.NormalizeModelId(modelId);
        PricingRule? matched = null;
        int bestPrefixLength = -1;

        foreach (PricingRule rule in Rules)
        {
            foreach (string prefix in rule.Prefixes)
            {
                if (prefix.Length > bestPrefixLength
                    && id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    matched = rule;
                    bestPrefixLength = prefix.Length;
                }
            }
        }

        if (matched is null)
        {
            rates = default;
            return false;
        }

        rates = promptTokens > LONG_CONTEXT_LIMIT && matched.LongContext is { } longRates
            ? longRates
            : matched.Standard;
        return true;
    }

    private static PricingRule Rule(string[] prefixes, ModelRates standard, ModelRates? longContext = null) =>
        new()
        {
            Prefixes    = prefixes,
            Standard    = standard,
            LongContext = longContext
        };

    private static ModelRates Rates(decimal input, decimal cachedInput, decimal output) =>
        new()
        {
            InputPerMillion       = input,
            CachedInputPerMillion = cachedInput,
            OutputPerMillion      = output
        };

    private sealed class PricingRule
    {
        public required string[]    Prefixes    { get; init; }
        public required ModelRates  Standard    { get; init; }
        public          ModelRates? LongContext { get; init; }
    }

    /// <summary>单个模型的百万 token 美元单价。</summary>
    internal readonly struct ModelRates
    {
        public decimal InputPerMillion       { get; init; }
        public decimal CachedInputPerMillion { get; init; }
        public decimal OutputPerMillion      { get; init; }
    }
}
