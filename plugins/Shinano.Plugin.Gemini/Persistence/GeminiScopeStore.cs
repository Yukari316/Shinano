using System.Collections.Concurrent;
using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using SqlSugar;

namespace Shinano.Plugin.Gemini.Persistence;

/// <summary>
/// Gemini 响应范围（仅白名单）：保存在全局 schema，初始化时读入内存，事件过滤只查内存，保存后立即生效。
/// 没有记录或名单为空时不响应任何该类会话。线程安全。
/// </summary>
public sealed class GeminiScopeStore
{
    private readonly IPluginInitializationContext                 _context;
    private readonly ILogger<GeminiScopeStore>                    _logger;
    private readonly ConcurrentDictionary<GeminiScopeKind, Rule>  _rules  = new();
    private readonly SemaphoreSlim                                _saving = new(1, 1);

    /// <summary>创建存储。</summary>
    public GeminiScopeStore(IPluginInitializationContext context, ILogger<GeminiScopeStore> logger)
    {
        _context = context;
        _logger  = logger;
    }

    private ISqlSugarClient Database => _context.Database.Global;

    /// <summary>创建缺失的表并读入全部白名单。</summary>
    public async Task LoadAsync()
    {
        Database.CodeFirst.InitTables<GeminiScopeRecord>();
        List<GeminiScopeRecord> records = await Database.Queryable<GeminiScopeRecord>().ToListAsync();
        _rules.Clear();
        foreach (GeminiScopeRecord record in records) _rules[record.Kind] = Rule.From(record);
    }

    /// <summary>获取一类会话的当前白名单；调用方不应修改返回的实例。</summary>
    public GeminiScopeRecord Get(GeminiScopeKind kind) =>
        _rules.TryGetValue(kind, out Rule? rule) ? rule.Record : new GeminiScopeRecord { Kind = kind };

    /// <summary>判断会话是否在白名单内；空名单返回 false。</summary>
    public bool Allows(GeminiScopeKind kind, long id) =>
        _rules.TryGetValue(kind, out Rule? rule) && rule.Ids.Contains(id);

    /// <summary>保存一类会话的白名单；调用方负责名单已去重、排序且号码为正数。</summary>
    public async Task SaveAsync(GeminiScopeRecord record)
    {
        await _saving.WaitAsync();
        try
        {
            await Database.Storageable(record).ExecuteCommandAsync();
            _rules[record.Kind] = Rule.From(record);
        }
        finally
        {
            _saving.Release();
        }

        _logger.LogInformation(
            "面板修改 Gemini 响应白名单：{Kind}，名单 {Ids}",
            record.Kind == GeminiScopeKind.Group ? "群聊" : "私聊",
            record.Ids.Length == 0 ? "为空" : string.Join("、", record.Ids));
    }

    private sealed class Rule
    {
        public required GeminiScopeRecord Record { get; init; }

        public required FrozenSet<long> Ids { get; init; }

        public static Rule From(GeminiScopeRecord record) =>
            new()
            {
                Record = record,
                Ids    = record.Ids.ToFrozenSet()
            };
    }
}
