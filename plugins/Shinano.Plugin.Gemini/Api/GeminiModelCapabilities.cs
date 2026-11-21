using System.Collections.Frozen;

namespace Shinano.Plugin.Gemini.Api;

/// <summary>
/// Gemini 模型思考深度（effort / <c>thinkingLevel</c>）能力映射。
/// 官方 <c>GET /v1beta/models</c> 只返回布尔字段 <c>thinking</c> 而非具体档位枚举，
/// 因此结合 API 返回的 <c>thinking</c> 标记与已知模型族规则表确定可选档位，未知新模型安全回退全集。
/// </summary>
public static class GeminiModelCapabilities
{
    /// <summary>默认模型 ID。</summary>
    public const string DEFAULT_MODEL = "gemini-4-hydrogen";

    /// <summary>默认思考深度。</summary>
    public const string DEFAULT_EFFORT = EFFORT_MEDIUM;

    /// <summary>不显式传递 <c>thinkingLevel</c>，由模型默认值决定。</summary>
    public const string EFFORT_DEFAULT = "default";

    /// <summary>最低思考深度。</summary>
    public const string EFFORT_MINIMAL = "minimal";

    /// <summary>低思考深度。</summary>
    public const string EFFORT_LOW = "low";

    /// <summary>中等思考深度。</summary>
    public const string EFFORT_MEDIUM = "medium";

    /// <summary>高思考深度。</summary>
    public const string EFFORT_HIGH = "high";

    /// <summary>极高思考深度。</summary>
    public const string EFFORT_XHIGH = "xhigh";

    private const string MODELS_PREFIX = "models/";

    private static readonly string[] Gemini4UltraEfforts = [EFFORT_DEFAULT, EFFORT_LOW, EFFORT_MEDIUM, EFFORT_HIGH, EFFORT_XHIGH];
    private static readonly string[] Gemini4Efforts      = [EFFORT_DEFAULT, EFFORT_LOW, EFFORT_MEDIUM, EFFORT_HIGH];
    private static readonly string[] Gemini3Efforts      = [EFFORT_DEFAULT, EFFORT_MINIMAL, EFFORT_LOW, EFFORT_MEDIUM, EFFORT_HIGH];
    private static readonly string[] LegacyThinkingOnly  = [EFFORT_DEFAULT];
    private static readonly string[] AllEfforts          = [EFFORT_DEFAULT, EFFORT_MINIMAL, EFFORT_LOW, EFFORT_MEDIUM, EFFORT_HIGH, EFFORT_XHIGH];

    private static readonly FrozenSet<string> ExplicitThinkingLevels = new[]
    {
        EFFORT_MINIMAL,
        EFFORT_LOW,
        EFFORT_MEDIUM,
        EFFORT_HIGH,
        EFFORT_XHIGH
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly EffortRule[] EffortRules =
    [
        new()
        {
            Prefixes = ["gemini-4-hydrogen", "gemini-4-neon"],
            Efforts  = Gemini4UltraEfforts
        },
        new()
        {
            Prefixes = ["gemini-4-", "gemini-4."],
            Efforts  = Gemini4Efforts
        },
        new()
        {
            Prefixes = ["gemini-3-", "gemini-3."],
            Efforts  = Gemini3Efforts
        },
        // Gemini 2.5 使用旧版 thinkingBudget 而非 thinkingLevel，只提供「默认（不传 thinkingLevel）」避免触发 400。
        new()
        {
            Prefixes = ["gemini-2.5-"],
            Efforts  = LegacyThinkingOnly
        },
        new()
        {
            Prefixes                = ["gemini-2.0-", "gemini-1.5-", "gemini-1.0-"],
            Efforts                 = [],
            OnlyWhenThinkingUnknown = true
        }
    ];

    /// <summary>去掉模型名可能带有的 <c>models/</c> 前缀并清理首尾空白。</summary>
    public static string NormalizeModelId(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return string.Empty;
        string trimmed = modelId.Trim();
        return trimmed.StartsWith(MODELS_PREFIX, StringComparison.OrdinalIgnoreCase)
            ? trimmed[MODELS_PREFIX.Length..]
            : trimmed;
    }

    /// <summary>按最长前缀匹配返回指定模型允许选择的 effort 档位列表；空数组表示该模型不支持思考配置。</summary>
    public static IReadOnlyList<string> GetAllowedEfforts(string modelId, bool? thinking = null)
    {
        if (thinking == false) return [];

        string id = NormalizeModelId(modelId);
        if (id.Length == 0) return AllEfforts;

        EffortRule? matched = null;
        int bestPrefixLength = -1;

        foreach (EffortRule rule in EffortRules)
        {
            if (rule.OnlyWhenThinkingUnknown && thinking is not null) continue;
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

        return matched?.Efforts ?? AllEfforts;
    }

    /// <summary>为指定模型计算默认 effort；不支持思考时返回空字符串。</summary>
    public static string DefaultEffortFor(string modelId, bool? thinking = null)
    {
        IReadOnlyList<string> allowed = GetAllowedEfforts(modelId, thinking);
        if (allowed.Count == 0) return string.Empty;
        if (allowed.Contains(EFFORT_MEDIUM, StringComparer.OrdinalIgnoreCase)) return EFFORT_MEDIUM;
        return allowed[0];
    }

    /// <summary>将请求的 effort 规范化为模型支持的合法值；不在支持列表内时回退到模型默认值。</summary>
    public static string NormalizeEffort(string modelId, string? requestedEffort, bool? thinking = null)
    {
        IReadOnlyList<string> allowed = GetAllowedEfforts(modelId, thinking);
        if (allowed.Count == 0) return string.Empty;

        string trimmed = requestedEffort?.Trim() ?? string.Empty;
        foreach (string option in allowed)
        {
            if (string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase)) return option;
        }

        return DefaultEffortFor(modelId, thinking);
    }

    /// <summary>判断该 effort 是否需要作为显式 <c>thinkingLevel</c> 传给 Gemini API。</summary>
    public static bool IsExplicitThinkingLevel(string? effort) =>
        effort is not null && ExplicitThinkingLevels.Contains(effort);

    /// <summary>该模型是否支持思考配置（含思考摘要与 effort）。</summary>
    public static bool SupportsThinking(string modelId, bool? thinking = null) =>
        GetAllowedEfforts(modelId, thinking).Count > 0;

    private sealed class EffortRule
    {
        public required string[] Prefixes                { get; init; }
        public required string[] Efforts                 { get; init; }
        public          bool     OnlyWhenThinkingUnknown { get; init; }
    }
}
