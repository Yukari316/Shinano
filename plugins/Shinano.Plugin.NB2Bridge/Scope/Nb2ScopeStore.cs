using System.Collections.Concurrent;
using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using SqlSugar;

namespace Shinano.Plugin.NB2Bridge.Scope;

/// <summary>
/// NB2 响应范围：保存在全局 schema，初始化时读入内存，事件过滤只查内存，保存后立即生效。没有记录的类别为空黑名单，即全部交付。线程安全。
/// </summary>
public sealed class Nb2ScopeStore
{
    private readonly IPluginInitializationContext             _context;
    private readonly ILogger<Nb2ScopeStore>                   _logger;
    private readonly ConcurrentDictionary<Nb2ScopeKind, Rule> _rules  = new();
    // 数据库与内存须按同一顺序更新，保存串行执行。
    private readonly SemaphoreSlim                            _saving = new(1, 1);

    /// <summary>创建存储。</summary>
    public Nb2ScopeStore(IPluginInitializationContext context, ILogger<Nb2ScopeStore> logger)
    {
        _context = context;
        _logger  = logger;
    }

    private ISqlSugarClient Database => _context.Database.Global;

    /// <summary>创建缺失的表并读入全部范围。</summary>
    public async Task LoadAsync()
    {
        Database.CodeFirst.InitTables<Nb2ScopeRecord>();
        List<Nb2ScopeRecord> records = await Database.Queryable<Nb2ScopeRecord>().ToListAsync();
        _rules.Clear();
        foreach (Nb2ScopeRecord record in records) _rules[record.Kind] = Rule.From(record);
    }

    /// <summary>一类会话的当前范围；调用方不应修改返回的实例。</summary>
    public Nb2ScopeRecord Get(Nb2ScopeKind kind) => _rules.TryGetValue(kind, out Rule? rule) ? rule.Record : new Nb2ScopeRecord { Kind = kind };

    /// <summary>该会话是否在响应范围内。</summary>
    /// <param name="kind">会话类别。</param>
    /// <param name="id">群号或好友 QQ 号。</param>
    public bool Allows(Nb2ScopeKind kind, long id) => !_rules.TryGetValue(kind, out Rule? rule) || rule.Record.Whitelist == rule.Ids.Contains(id);

    /// <summary>保存一类会话的范围。调用方负责名单已去重、排序且号码为正数。</summary>
    public async Task SaveAsync(Nb2ScopeRecord record)
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
            "面板修改 NB2 响应范围：{Kind}使用{Mode}，名单 {Ids}",
            record.Kind == Nb2ScopeKind.Group ? "群聊" : "私聊",
            record.Whitelist ? "白名单" : "黑名单",
            record.Ids.Length == 0 ? "为空" : string.Join("、", record.Ids));
    }

    private sealed class Rule
    {
        public required Nb2ScopeRecord Record { get; init; }

        public required FrozenSet<long> Ids { get; init; }

        public static Rule From(Nb2ScopeRecord record) =>
            new()
            {
                Record = record,
                Ids    = record.Ids.ToFrozenSet()
            };
    }
}
