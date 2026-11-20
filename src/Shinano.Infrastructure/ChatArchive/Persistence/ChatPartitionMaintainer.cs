using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shinano.Infrastructure.Database;

namespace Shinano.Infrastructure.ChatArchive.Persistence;

/// <summary>
/// 按配置时区的自然日维护聊天表分区：预建昨天到未来若干天的分区，删除整段早于保留期的分区。
/// </summary>
/// <remarks>
/// 修改时区不重建已有分区。新分区的下界衔接已有分区的最大上界（相差不足一天时），
/// 因此时区切换当天的分区可能不足或超过 24 小时，但时间轴上不留空隙也不重叠。
/// </remarks>
public sealed partial class ChatPartitionMaintainer
{
    private const string PARTITION_PREFIX  = ChatMessageTable.TABLE_NAME + "_p";
    private const string DATE_FORMAT       = "yyyyMMdd";
    private const string BOUND_FORMAT      = "yyyy-MM-dd HH:mm:sszzz";
    private const int    PAST_DAYS_TO_KEEP = 1;

    private static readonly string[] BoundParseFormats = ["yyyy-MM-dd HH:mm:sszz", "yyyy-MM-dd HH:mm:ss.FFFFFFzz"];

    private readonly ShinanoDatabase                  _database;
    private readonly ILogger<ChatPartitionMaintainer> _logger;

    /// <summary>创建维护器。</summary>
    public ChatPartitionMaintainer(ShinanoDatabase database, ILogger<ChatPartitionMaintainer> logger)
    {
        _database = database;
        _logger   = logger;
    }

    /// <summary>
    /// 确保从昨天到未来 <paramref name="daysAhead" /> 天的每个自然日都被分区覆盖。
    /// </summary>
    /// <returns>新建的分区数。</returns>
    public async Task<int> EnsurePartitionsAsync(
        long              selfId,
        DateTimeOffset    now,
        TimeZoneInfo      timeZone,
        int               daysAhead,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(daysAhead);
        string schema = SchemaNames.RequireIdentifier(_database.Schemas.Account(selfId));
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        List<ChatPartition> partitions = await ListAsync(connection, schema, cancellationToken);
        DateOnly today   = LocalDate(now, timeZone);
        int      created = 0;

        for (DateOnly day = today.AddDays(-PAST_DAYS_TO_KEEP); day <= today.AddDays(daysAhead); day = day.AddDays(1))
        {
            DateTimeOffset dayStart = StartOfDay(day, timeZone);
            DateTimeOffset dayEnd   = StartOfDay(day.AddDays(1), timeZone);
            DateTimeOffset? latest  = partitions.Count == 0 ? null : partitions.Max(item => item.To);
            if (latest >= dayEnd) continue;

            // 与已有分区相接时从其上界开始，避免时区切换在两个分区之间留下空隙。
            DateTimeOffset from = latest is { } upper && upper > dayStart - TimeSpan.FromDays(1) ? upper : dayStart;
            string name = UniqueName(partitions, PARTITION_PREFIX + day.ToString(DATE_FORMAT, CultureInfo.InvariantCulture));

            // SQL_NEED: 创建分区（partition of ... for values）是 DDL，SqlSugar 没有对应接口。
            await using NpgsqlCommand command = new(
                $"create table {schema}.{name} partition of {schema}.{ChatMessageTable.TABLE_NAME} " +
                $"for values from ('{FormatBound(from)}') to ('{FormatBound(dayEnd)}')",
                connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            partitions.Add(new ChatPartition { Name = name, From = from, To = dayEnd });
            created++;
            _logger.LogInformation("已创建聊天分区 {Schema}.{Partition}", schema, name);
        }

        return created;
    }

    /// <summary>
    /// 聊天记录的过期截止时刻：今天往前 <paramref name="retentionDays" /> 天的零点（按 <paramref name="timeZone" />）。
    /// 聊天分区与聊天媒体共用这一时刻，保证还在库中的消息引用的媒体不会先被删除。
    /// </summary>
    public static DateTimeOffset RetentionCutoff(DateTimeOffset now, TimeZoneInfo timeZone, int retentionDays) =>
        StartOfDay(LocalDate(now, timeZone).AddDays(-retentionDays), timeZone);

    /// <summary>
    /// 删除上界不晚于 <see cref="RetentionCutoff" /> 的分区。
    /// </summary>
    /// <returns>删除的分区数。</returns>
    public async Task<int> DropExpiredAsync(
        long              selfId,
        DateTimeOffset    now,
        TimeZoneInfo      timeZone,
        int               retentionDays,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retentionDays);
        string schema = SchemaNames.RequireIdentifier(_database.Schemas.Account(selfId));
        DateTimeOffset cutoff = RetentionCutoff(now, timeZone, retentionDays);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        int dropped = 0;
        foreach (ChatPartition partition in await ListAsync(connection, schema, cancellationToken))
        {
            if (partition.To > cutoff) continue;

            // SQL_NEED: 删除分区表是 DDL，SqlSugar 没有对应接口。
            await using NpgsqlCommand command = new(
                $"drop table {schema}.{SchemaNames.RequireIdentifier(partition.Name)}",
                connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            dropped++;
            _logger.LogInformation("已删除过期聊天分区 {Schema}.{Partition}", schema, partition.Name);
        }

        return dropped;
    }

    // 只供集成测试核对分区边界；宿主运行路径在维护时直接使用同一连接上的私有重载。
    internal async Task<List<ChatPartition>> ListAsync(long selfId, CancellationToken cancellationToken)
    {
        string schema = SchemaNames.RequireIdentifier(_database.Schemas.Account(selfId));
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await ListAsync(connection, schema, cancellationToken);
    }

    /// <summary>返回 <paramref name="now" /> 在 <paramref name="timeZone" /> 中的日期。</summary>
    public static DateOnly LocalDate(DateTimeOffset now, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);

    /// <summary>
    /// 返回 <paramref name="day" /> 在 <paramref name="timeZone" /> 中的零点（UTC 表示）。
    /// 零点因夏令时不存在时取当天第一个有效时刻。
    /// </summary>
    public static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo timeZone)
    {
        DateTime local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
    }

    private static async Task<List<ChatPartition>> ListAsync(
        NpgsqlConnection  connection,
        string            schema,
        CancellationToken cancellationToken)
    {
        // SQL_NEED: 分区与边界只能从系统目录 pg_inherits、pg_get_expr 读取，SqlSugar 没有对应接口。
        await using NpgsqlCommand command = new(
            """
            select c.relname, pg_get_expr(c.relpartbound, c.oid)
            from pg_inherits i
            join pg_class c on c.oid = i.inhrelid
            join pg_class p on p.oid = i.inhparent
            join pg_namespace n on n.oid = p.relnamespace
            where n.nspname = @schema and p.relname = @table
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", ChatMessageTable.TABLE_NAME);
        List<ChatPartition> partitions = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Match match = BoundPattern().Match(reader.GetString(1));
            if (!match.Success) continue;

            partitions.Add(
                new ChatPartition
                {
                    Name = reader.GetString(0),
                    From = ParseBound(match.Groups["from"].Value),
                    To   = ParseBound(match.Groups["to"].Value)
                });
        }

        return partitions;
    }

    // 时区切换后同一日期可能需要第二个分区补齐剩余时段。
    private static string UniqueName(List<ChatPartition> partitions, string baseName)
    {
        string name     = baseName;
        int    sequence = 1;
        while (partitions.Any(item => item.Name == name))
        {
            sequence++;
            name = $"{baseName}_{sequence}";
        }

        return name;
    }

    private static string FormatBound(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(BOUND_FORMAT, CultureInfo.InvariantCulture);

    // 连接的会话时区固定为 UTC，pg_get_expr 输出形如 '2026-10-17 16:00:00+00'。
    private static DateTimeOffset ParseBound(string value) =>
        DateTimeOffset.ParseExact(value, BoundParseFormats, CultureInfo.InvariantCulture, DateTimeStyles.None);

    [GeneratedRegex(@"FROM \('(?<from>[^']+)'\) TO \('(?<to>[^']+)'\)")]
    private static partial Regex BoundPattern();
}

/// <summary>聊天表的一个已存在分区。</summary>
public sealed class ChatPartition
{
    /// <summary>分区表名。</summary>
    public required string Name { get; init; }

    /// <summary>下界（含），UTC。</summary>
    public required DateTimeOffset From { get; init; }

    /// <summary>上界（不含），UTC。</summary>
    public required DateTimeOffset To { get; init; }
}
