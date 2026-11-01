using Shinano.Framework.Plugins;
using Shinano.Plugin.Qa.Entities;
using Shinano.Plugin.Qa.Models;
using SqlSugar;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shinano.Plugin.Qa;

/// <summary>
/// 词条与禁止提问词的存储。词条按群缓存在内存，同一进程内的增删改使对应群的缓存失效；一个部署只有一个宿主进程。
/// 线程安全。
/// </summary>
public sealed class QaStore
{
    /// <summary>持久媒体引用键的前缀，后接词条版本的媒体键。</summary>
    public const string MEDIA_REF_PREFIX = "qa:";

    private const string FORBIDDEN = "嘻嘻你不能设置这个问题";

    private static readonly JsonSerializerOptions AnswerJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters             = { new JsonStringEnumConverter() }
    };

    private readonly IPluginDatabase                                       _database;
    private readonly TimeProvider                                          _time;
    private readonly ConcurrentDictionary<long, IReadOnlyList<QaEntry>> _groups = new();

    /// <summary>创建存储。</summary>
    public QaStore(IPluginInitializationContext context, TimeProvider time)
    {
        _database = context.Database;
        _time     = time;
    }

    /// <summary>创建缺失的表。</summary>
    public void Initialize()
    {
        _database.Account.CodeFirst.InitTables<QaEntryRecord>();
        _database.Global.CodeFirst.InitTables<QaForbiddenQuestionRecord>();
    }

    /// <summary>词条版本的持久媒体引用键。</summary>
    public static string RefKey(string mediaKey) => MEDIA_REF_PREFIX + mediaKey;

    /// <summary>生成新的媒体键。</summary>
    public static string NewMediaKey() => Guid.NewGuid().ToString("N");

    /// <summary>群的全部词条，越新越靠前。</summary>
    public async Task<IReadOnlyList<QaEntry>> GroupAsync(long groupId)
    {
        if (_groups.TryGetValue(groupId, out IReadOnlyList<QaEntry>? cached)) return cached;

        List<QaEntryRecord> records = await _database.Account.Queryable<QaEntryRecord>()
                                                      .Where(record => record.GroupId == groupId)
                                                      .OrderByDescending(record => record.CreatedAt)
                                                      .OrderByDescending(record => record.Id)
                                                      .ToListAsync();
        IReadOnlyList<QaEntry> entries = [.. records.Select(ToEntry)];
        _groups[groupId] = entries;
        return entries;
    }

    /// <summary>查找同群、同匹配方式、同问题的词条。</summary>
    public async Task<QaEntry?> FindAsync(long groupId, QaMatchType matchType, string question) =>
        (await GroupAsync(groupId)).FirstOrDefault(entry => entry.MatchType == matchType && entry.Question == question);

    /// <summary>新增词条。</summary>
    /// <returns>新词条；同群同匹配方式的问题已存在（含并发录入）时为 null。</returns>
    public async Task<QaEntry?> AddAsync(long groupId, QaMatchType matchType, string question, IReadOnlyList<QaAnswerPart> answer, string mediaKey, long createdBy)
    {
        if (await FindAsync(groupId, matchType, question) is not null) return null;

        QaEntryRecord record = new()
        {
            GroupId   = groupId,
            MatchType = (int)matchType,
            Question  = question,
            Answer    = JsonSerializer.Serialize(answer, AnswerJson),
            MediaKey  = mediaKey,
            CreatedBy = createdBy,
            CreatedAt = _time.GetUtcNow(),
            Version   = 1
        };
        try
        {
            record.Id = await _database.Account.Insertable(record).ExecuteReturnBigIdentityAsync();
        }
        catch (Exception)
        {
            // 唯一约束冲突（并发录入同一问题）按重复处理，其他错误照常抛出。
            _groups.TryRemove(groupId, out _);
            if (await FindAsync(groupId, matchType, question) is not null) return null;
            throw;
        }

        _groups.TryRemove(groupId, out _);
        return ToEntry(record);
    }

    /// <summary>用新回答覆盖词条，覆盖后的词条视为最新创建。</summary>
    /// <returns>是否覆盖；词条已被他人修改或删除时为 false。</returns>
    public async Task<bool> ReplaceAsync(QaEntry existing, IReadOnlyList<QaAnswerPart> answer, string mediaKey, long createdBy)
    {
        int updated = await _database.Account.Updateable<QaEntryRecord>()
                                     .SetColumns(record => new QaEntryRecord
                                     {
                                         Answer    = JsonSerializer.Serialize(answer, AnswerJson),
                                         MediaKey  = mediaKey,
                                         CreatedBy = createdBy,
                                         CreatedAt = _time.GetUtcNow(),
                                         Version   = existing.Version + 1
                                     })
                                     .Where(record => record.Id == existing.Id && record.Version == existing.Version)
                                     .ExecuteCommandAsync();
        _groups.TryRemove(existing.GroupId, out _);
        return updated == 1;
    }

    /// <summary>删除同群、同匹配方式、同问题的词条。</summary>
    /// <returns>被删除的词条；不存在时为 null。</returns>
    public async Task<QaEntry?> DeleteAsync(long groupId, QaMatchType matchType, string question) =>
        await FindAsync(groupId, matchType, question) is { } entry ? await DeleteByIdAsync(entry.Id) : null;

    /// <summary>按 ID 删除词条。</summary>
    /// <returns>被删除的词条；不存在时为 null。</returns>
    public async Task<QaEntry?> DeleteByIdAsync(long id)
    {
        QaEntryRecord? record = await _database.Account.Queryable<QaEntryRecord>().FirstAsync(item => item.Id == id);
        if (record is null) return null;

        int deleted = await _database.Account.Deleteable<QaEntryRecord>().Where(item => item.Id == id).ExecuteCommandAsync();
        _groups.TryRemove(record.GroupId, out _);
        return deleted == 1 ? ToEntry(record) : null;
    }

    /// <summary>按条件列出当前账号的词条（面板用），越新越靠前。</summary>
    public async Task<IReadOnlyList<QaEntry>> QueryAsync(long? groupId, QaMatchType? matchType, string? keyword)
    {
        long group = groupId.GetValueOrDefault();
        int type = (int)matchType.GetValueOrDefault();
        string text = keyword?.Trim() ?? string.Empty;
        List<QaEntryRecord> records = await _database.Account.Queryable<QaEntryRecord>()
                                                      .WhereIF(groupId is not null, record => record.GroupId == group)
                                                      .WhereIF(matchType is not null, record => record.MatchType == type)
                                                      .WhereIF(text.Length > 0, record => record.Question.Contains(text))
                                                      .OrderByDescending(record => record.CreatedAt)
                                                      .OrderByDescending(record => record.Id)
                                                      .ToListAsync();
        return [.. records.Select(ToEntry)];
    }

    /// <summary>媒体是否被当前账号的某个词条使用（面板读取媒体前核对）。</summary>
    public async Task<bool> UsesMediaAsync(long mediaId)
    {
        string end = $"\"mediaId\":{mediaId}}}";
        string middle = $"\"mediaId\":{mediaId},";
        return await _database.Account.Queryable<QaEntryRecord>()
                              .AnyAsync(record => record.Answer.Contains(end) || record.Answer.Contains(middle));
    }

    /// <summary>录入前的问题检查：长度、禁止提问词（与条目完全相同）与正则编译。</summary>
    /// <returns>拒绝时告知录入人的文字；可以录入时为 null。</returns>
    public async Task<string?> RejectQuestionAsync(string question, QaMatchType matchType)
    {
        if (question.Length > QaRecordingParser.MAX_QUESTION_LENGTH) return $"问题最长 {QaRecordingParser.MAX_QUESTION_LENGTH} 个字";
        if (await IsForbiddenAsync(question)) return FORBIDDEN;
        return matchType == QaMatchType.Regex && QaMatcher.Validate(question) is { } error ? $"正则表达式有误：{error}" : null;
    }

    /// <summary>全部禁止提问词。</summary>
    public async Task<IReadOnlyList<QaForbiddenQuestionRecord>> ForbiddenAsync() =>
        await _database.Global.Queryable<QaForbiddenQuestionRecord>().OrderBy(record => record.Question).ToListAsync();

    /// <summary>问题文本是否与某个禁止提问词完全相同。</summary>
    public async Task<bool> IsForbiddenAsync(string question) =>
        await _database.Global.Queryable<QaForbiddenQuestionRecord>().AnyAsync(record => record.Question == question);

    /// <summary>新增禁止提问词。</summary>
    /// <returns>是否新增；已存在时为 false。</returns>
    public async Task<bool> AddForbiddenAsync(string question)
    {
        if (await IsForbiddenAsync(question)) return false;
        await _database.Global.Insertable(new QaForbiddenQuestionRecord { Question = question }).ExecuteCommandAsync();
        return true;
    }

    /// <summary>删除禁止提问词。</summary>
    /// <returns>是否删除。</returns>
    public async Task<bool> DeleteForbiddenAsync(long id) =>
        await _database.Global.Deleteable<QaForbiddenQuestionRecord>().Where(record => record.Id == id).ExecuteCommandAsync() == 1;

    private static QaEntry ToEntry(QaEntryRecord record) =>
        new()
        {
            Id        = record.Id,
            GroupId   = record.GroupId,
            MatchType = (QaMatchType)record.MatchType,
            Question  = record.Question,
            Answer    = JsonSerializer.Deserialize<List<QaAnswerPart>>(record.Answer, AnswerJson) ?? [],
            MediaKey  = record.MediaKey,
            CreatedBy = record.CreatedBy,
            CreatedAt = record.CreatedAt,
            Version   = record.Version
        };
}
