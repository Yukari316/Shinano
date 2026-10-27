using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shinano.Monitor.Probing.Models;

namespace Shinano.Monitor.Probing;

/// <summary>
/// 探测记录：追加写入 <c>data/probes-&lt;日期&gt;.jsonl</c>（部署时区的自然日），内存中保留最近 7 天供状态页计算。
/// 只由 <see cref="ProbeService" /> 的单个循环写入；读取方取得的是快照。
/// </summary>
public sealed class ProbeStore
{
    /// <summary>记录保留时长。</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private const string FILE_PREFIX = "probes-";
    private const string FILE_SUFFIX = ".jsonl";
    private const string DATE_FORMAT = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters             = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string                  _directory;
    private readonly TimeZoneInfo            _timeZone;
    private readonly ILogger<ProbeStore>     _logger;
    private readonly Lock                    _lock    = new();
    private readonly List<ProbeRecord>       _records = [];

    /// <summary>创建记录存储，目录不存在时创建。</summary>
    public ProbeStore(string directory, TimeZoneInfo timeZone, ILogger<ProbeStore> logger)
    {
        _directory = directory;
        _timeZone  = timeZone;
        _logger    = logger;
        Directory.CreateDirectory(directory);
    }

    /// <summary>删除超过保留期的文件，并读入保留期内的记录。无法解析的行记录警告后跳过。</summary>
    public void Load(DateTimeOffset now)
    {
        Prune(now);
        DateTimeOffset since = now - Retention;
        List<ProbeRecord> loaded = [];
        foreach (string path in Directory.EnumerateFiles(_directory, FILE_PREFIX + "*" + FILE_SUFFIX).Order(StringComparer.Ordinal))
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0) continue;
                try
                {
                    if (JsonSerializer.Deserialize<ProbeRecord>(line, JsonOptions) is { } record && record.Time >= since)
                        loaded.Add(record);
                }
                catch (JsonException)
                {
                    _logger.LogWarning("跳过无法解析的探测记录：{File}", Path.GetFileName(path));
                }
            }

        lock (_lock)
        {
            _records.Clear();
            _records.AddRange(loaded.OrderBy(record => record.Time));
        }
    }

    /// <summary>追加一轮探测结果，并丢弃内存中超过保留期的记录。</summary>
    public void Append(IReadOnlyList<ProbeRecord> records, DateTimeOffset now)
    {
        if (records.Count == 0) return;

        string path = Path.Combine(_directory, FileName(records[0].Time));
        File.AppendAllLines(path, records.Select(record => JsonSerializer.Serialize(record, JsonOptions)));
        DateTimeOffset since = now - Retention;
        lock (_lock)
        {
            _records.AddRange(records);
            _records.RemoveAll(record => record.Time < since);
        }
    }

    /// <summary>保留期内全部记录的快照，按时间排序。</summary>
    public IReadOnlyList<ProbeRecord> Snapshot()
    {
        lock (_lock)
            return [.. _records];
    }

    /// <summary>删除日期早于保留期的记录文件。</summary>
    public void Prune(DateTimeOffset now)
    {
        DateOnly oldest = LocalDate(now - Retention);
        foreach (string path in Directory.EnumerateFiles(_directory, FILE_PREFIX + "*" + FILE_SUFFIX))
        {
            string date = Path.GetFileName(path)[FILE_PREFIX.Length..^FILE_SUFFIX.Length];
            if (!DateOnly.TryParseExact(date, DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
                || day >= oldest) continue;
            try
            {
                File.Delete(path);
                _logger.LogInformation("已删除过期的探测记录 {File}", Path.GetFileName(path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "无法删除探测记录 {File}", Path.GetFileName(path));
            }
        }
    }

    private string FileName(DateTimeOffset time) =>
        FILE_PREFIX + LocalDate(time).ToString(DATE_FORMAT, CultureInfo.InvariantCulture) + FILE_SUFFIX;

    private DateOnly LocalDate(DateTimeOffset time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, _timeZone).DateTime);
}
