using System.Globalization;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Gemini.Api;
using SqlSugar;

namespace Shinano.Plugin.Gemini.Persistence;

/// <summary>
/// Gemini 用量统计存储：按部署时区日期与模型在 PostgreSQL 全局 schema 原子累加请求数、token 消耗、缓存命中与估算成本（USD）。
/// 记账失败仅记日志，不影响聊天回复流程。线程安全。
/// </summary>
public sealed class GeminiUsageStore
{
    private const string DATE_FORMAT = "yyyy-MM-dd";

    // SQL_NEED: 并发请求对同一「日期 × 模型」行原子累加计数，需要 INSERT ... ON CONFLICT DO UPDATE，SqlSugar 实体 API 无法表达累加式 upsert。
    private const string UPSERT_SQL = """
        INSERT INTO gemini_usage_daily (
            stat_date, model, request_count, success_count,
            prompt_tokens, cached_tokens, candidates_tokens, thoughts_tokens, total_tokens, cost_usd
        )
        VALUES (
            @stat_date, @model, 1, @success,
            @prompt_tokens, @cached_tokens, @candidates_tokens, @thoughts_tokens, @total_tokens, @cost_usd
        )
        ON CONFLICT (stat_date, model) DO UPDATE SET
            request_count     = gemini_usage_daily.request_count + 1,
            success_count     = gemini_usage_daily.success_count + EXCLUDED.success_count,
            prompt_tokens     = gemini_usage_daily.prompt_tokens + EXCLUDED.prompt_tokens,
            cached_tokens     = gemini_usage_daily.cached_tokens + EXCLUDED.cached_tokens,
            candidates_tokens = gemini_usage_daily.candidates_tokens + EXCLUDED.candidates_tokens,
            thoughts_tokens   = gemini_usage_daily.thoughts_tokens + EXCLUDED.thoughts_tokens,
            total_tokens      = gemini_usage_daily.total_tokens + EXCLUDED.total_tokens,
            cost_usd          = gemini_usage_daily.cost_usd + EXCLUDED.cost_usd
        """;

    private readonly IPluginInitializationContext _context;
    private readonly TimeProvider                 _time;
    private readonly ILogger<GeminiUsageStore>    _logger;

    /// <summary>创建用量存储。</summary>
    public GeminiUsageStore(
        IPluginInitializationContext context,
        TimeProvider                 time,
        ILogger<GeminiUsageStore>    logger)
    {
        _context = context;
        _time    = time;
        _logger  = logger;
    }

    private ISqlSugarClient Database => _context.Database.Global;

    /// <summary>初始化缺失的用量统计表。</summary>
    public Task LoadAsync()
    {
        Database.CodeFirst.InitTables<GeminiUsageRecord>();
        return Task.CompletedTask;
    }

    /// <summary>记录一次 Gemini API 调用的请求状态、token 消耗与估算成本（USD）。</summary>
    public async Task RecordAsync(string model, bool succeeded, GeminiUsageMetadata? usage, decimal costUsd)
    {
        string normalizedModel = GeminiModelCapabilities.NormalizeModelId(model);
        if (normalizedModel.Length == 0) normalizedModel = GeminiModelCapabilities.DEFAULT_MODEL;

        string statDate = TodayDateString();
        long prompt = Math.Max(0, usage?.PromptTokens ?? 0);
        long cached = Math.Clamp(usage?.CachedTokens ?? 0, 0, (int)Math.Min(int.MaxValue, prompt));
        long candidates = Math.Max(0, usage?.CandidatesTokens ?? 0);
        long thoughts = Math.Max(0, usage?.ThoughtsTokens ?? 0);
        long total = Math.Max(0, usage?.TotalTokens ?? (prompt + candidates + thoughts));

        try
        {
            await Database.Ado.ExecuteCommandAsync(
                UPSERT_SQL,
                new
                {
                    stat_date         = statDate,
                    model             = normalizedModel,
                    success           = succeeded ? 1L : 0L,
                    prompt_tokens     = prompt,
                    cached_tokens     = cached,
                    candidates_tokens = candidates,
                    thoughts_tokens   = thoughts,
                    total_tokens      = total,
                    cost_usd          = Math.Max(0m, costUsd)
                });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "写入 Gemini 用量统计失败：date={Date}，model={Model}", statDate, normalizedModel);
        }
    }

    /// <summary>按天数范围查询每日分模型明细；<paramref name="days" /> 小于等于 0 时返回全部历史。</summary>
    public async Task<IReadOnlyList<GeminiUsageRecord>> QueryAsync(int days)
    {
        ISugarQueryable<GeminiUsageRecord> query = Database.Queryable<GeminiUsageRecord>();
        if (days > 0)
        {
            DateTimeOffset localNow = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _context.TimeZone);
            string since = localNow.Date.AddDays(-(days - 1)).ToString(DATE_FORMAT, CultureInfo.InvariantCulture);
            query = query.Where("stat_date >= @since", new { since });
        }

        List<GeminiUsageRecord> rows = await query.OrderBy(record => record.StatDate, OrderByType.Desc)
                                                  .OrderBy(record => record.Model)
                                                  .ToListAsync();
        return rows;
    }

    private string TodayDateString() =>
        TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _context.TimeZone)
                    .ToString(DATE_FORMAT, CultureInfo.InvariantCulture);
}
