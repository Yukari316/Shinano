namespace Shinano.Plugin.Gemini.Api;

/// <summary>
/// Gemini 模型内置公开单价表（USD / 100 万 tokens），用于在日志与管理面板中估算请求成本。
/// 输出单价同时覆盖可见回答与思考 token；缓存命中输入按对应模型的隐式缓存折扣单价计费。
/// </summary>
public static class GeminiPricing
{
    private const decimal TOKENS_PER_MILLION = 1_000_000m;
    private const int     LONG_CONTEXT_LIMIT = 200_000;

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

    /// <summary>尝试查找模型对应的百万 token 单价（USD）。</summary>
    internal static bool TryGetRates(string modelId, int promptTokens, out ModelRates rates)
    {
        string id = GeminiModelCapabilities.NormalizeModelId(modelId);
        bool longContext = promptTokens > LONG_CONTEXT_LIMIT;

        if (id.StartsWith("gemini-4-neon", StringComparison.OrdinalIgnoreCase))
        {
            rates = new ModelRates { InputPerMillion = 3.00m, CachedInputPerMillion = 0.15m, OutputPerMillion = 15.00m };
            return true;
        }

        if (id.StartsWith("gemini-4-hydrogen", StringComparison.OrdinalIgnoreCase))
        {
            rates = new ModelRates { InputPerMillion = 1.50m, CachedInputPerMillion = 0.075m, OutputPerMillion = 7.50m };
            return true;
        }

        if (id.StartsWith("gemini-4-carbon", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("gemini-4-argon", StringComparison.OrdinalIgnoreCase))
        {
            rates = new ModelRates { InputPerMillion = 2.00m, CachedInputPerMillion = 0.10m, OutputPerMillion = 10.00m };
            return true;
        }

        if (id.StartsWith("gemini-3.8-flash", StringComparison.OrdinalIgnoreCase))
        {
            rates = new ModelRates { InputPerMillion = 0.75m, CachedInputPerMillion = 0.075m, OutputPerMillion = 3.75m };
            return true;
        }

        if (id.StartsWith("gemini-3.6-flash", StringComparison.OrdinalIgnoreCase))
        {
            rates = new ModelRates { InputPerMillion = 1.50m, CachedInputPerMillion = 0.075m, OutputPerMillion = 7.50m };
            return true;
        }

        if (id.StartsWith("gemini-3.1-pro", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("gemini-3-pro", StringComparison.OrdinalIgnoreCase))
        {
            rates = longContext
                ? new ModelRates { InputPerMillion = 4.00m, CachedInputPerMillion = 0.40m, OutputPerMillion = 18.00m }
                : new ModelRates { InputPerMillion = 2.00m, CachedInputPerMillion = 0.20m, OutputPerMillion = 12.00m };
            return true;
        }

        if (id.StartsWith("gemini-3-flash", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("gemini-3.", StringComparison.OrdinalIgnoreCase))
        {
            rates = new ModelRates { InputPerMillion = 0.50m, CachedInputPerMillion = 0.05m, OutputPerMillion = 3.00m };
            return true;
        }

        if (id.StartsWith("gemini-2.5-pro", StringComparison.OrdinalIgnoreCase))
        {
            rates = longContext
                ? new ModelRates { InputPerMillion = 2.50m, CachedInputPerMillion = 0.25m, OutputPerMillion = 15.00m }
                : new ModelRates { InputPerMillion = 1.25m, CachedInputPerMillion = 0.125m, OutputPerMillion = 10.00m };
            return true;
        }

        if (id.StartsWith("gemini-2.5-flash-lite", StringComparison.OrdinalIgnoreCase))
        {
            rates = new ModelRates { InputPerMillion = 0.10m, CachedInputPerMillion = 0.01m, OutputPerMillion = 0.40m };
            return true;
        }

        if (id.StartsWith("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase))
        {
            rates = new ModelRates { InputPerMillion = 0.30m, CachedInputPerMillion = 0.03m, OutputPerMillion = 2.50m };
            return true;
        }

        if (id.StartsWith("gemini-2.0-flash-lite", StringComparison.OrdinalIgnoreCase))
        {
            rates = new ModelRates { InputPerMillion = 0.075m, CachedInputPerMillion = 0.0075m, OutputPerMillion = 0.30m };
            return true;
        }

        if (id.StartsWith("gemini-2.0-flash", StringComparison.OrdinalIgnoreCase))
        {
            rates = new ModelRates { InputPerMillion = 0.10m, CachedInputPerMillion = 0.025m, OutputPerMillion = 0.40m };
            return true;
        }

        rates = default;
        return false;
    }

    /// <summary>单个模型的百万 token 美元单价。</summary>
    internal readonly struct ModelRates
    {
        public decimal InputPerMillion       { get; init; }
        public decimal CachedInputPerMillion { get; init; }
        public decimal OutputPerMillion      { get; init; }
    }
}
