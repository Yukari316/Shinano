using System.Globalization;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shinano.Host.Bot;
using Shinano.Host.Bot.Models;
using Shinano.Host.Browser;
using Shinano.Host.Plugins.Capabilities;
using Shinano.Host.Plugins.Entities;
using Shinano.Host.Plugins.Models;
using Shinano.Host.Restart;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Database.Entities;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Health.Models;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Storage;
using SqlSugar;

namespace Shinano.Host.Plugins;

/// <summary>
/// 删除插件包并选择清除数据时，在重启确认成功后清除插件数据：声明的表与结构版本记录、持久媒体引用、插件 bucket、
/// 配置与启用状态、机密文件与浏览器用户目录，最后报告可能的残留表。
/// <list type="bullet">
/// <item>任务保存在部署数据目录的 <c>plugin-purge/&lt;id&gt;.json</c>，在基础设施就绪后于后台执行；失败的步骤在下次启动时重试。</item>
/// <item>执行前发现同 ID 的插件已重新安装时放弃剩余步骤，避免删除新插件的数据。</item>
/// <item>表只按清单声明的表名全字匹配删除；名字以插件前缀开头但未声明的表只报告，不删除。</item>
/// </list>
/// 同一时间只执行一轮。
/// </summary>
public sealed class PluginDataPurger : BackgroundService
{
    private const string DIRECTORY_NAME = "plugin-purge";
    private const string FILE_EXTENSION = ".json";
    private const string FAILED_CODE    = "PLUGIN-PURGE-FAILED";
    private const string SCHEMA_VERSION = "schema_version";
    private const string SEPARATOR      = "；";

    private readonly HostInfrastructure        _infrastructure;
    private readonly PluginRuntimeSettings     _settings;
    private readonly BrowserService            _browser;
    private readonly BotAccessor               _bot;
    private readonly SuperUserNotifier         _notifier;
    private readonly RuntimeHealth             _health;
    private readonly TimeProvider              _time;
    private readonly ILogger<PluginDataPurger> _logger;
    private readonly SemaphoreSlim             _gate            = new(1, 1);
    private readonly Lock                      _lock            = new();
    private readonly List<NotificationCard>    _deferredNotices = [];

    /// <summary>创建清除服务。</summary>
    public PluginDataPurger(
        HostInfrastructure        infrastructure,
        PluginRuntimeSettings     settings,
        BrowserService            browser,
        BotAccessor               bot,
        SuperUserNotifier         notifier,
        RuntimeHealth             health,
        TimeProvider              time,
        ILogger<PluginDataPurger> logger)
    {
        _infrastructure = infrastructure;
        _settings       = settings;
        _browser        = browser;
        _bot            = bot;
        _notifier       = notifier;
        _health         = health;
        _time           = time;
        _logger         = logger;
        Directory.CreateDirectory(Root);
        _health.Changed += OnHealthChanged;
    }

    private string Root => Path.Combine(_settings.DataDirectory, DIRECTORY_NAME);

    /// <summary>
    /// 建立一个插件的清除任务（等待执行）。只在删除插件包的重启确认成功（或因故障模式未经验证）后调用；
    /// 同一插件已有任务时以新任务替换。
    /// </summary>
    public void Schedule(PluginChange change, PluginDataScope scope)
    {
        PluginPurge purge = new()
        {
            PluginId  = change.PluginId,
            Name      = change.Name,
            Scope     = scope,
            CreatedAt = _time.GetUtcNow(),
            Steps     = [.. Enum.GetValues<PluginPurgeStepKind>().Select(kind => new PluginPurgeStep { Kind = kind })]
        };
        Save(purge);
        _logger.LogInformation("已建立插件 {PluginId} 的数据清除任务，基础设施就绪后执行", change.PluginId);
    }

    /// <summary>在后台执行全部未完成的任务；基础设施未就绪时什么都不做，下次启动再执行。</summary>
    public void RequestRun()
    {
        // 不传递调用方的执行上下文：SqlSugarScope 以 AsyncLocal 分配客户端，继承后会与调用方共用数据库连接。
        using (ExecutionContext.SuppressFlow())
            _ = Task.Run(() => RunPendingAsync(CancellationToken.None));
    }

    /// <summary>未关闭显示的清除结果，按建立时间排列。</summary>
    public IReadOnlyList<PluginPurge> Visible()
    {
        lock (_lock)
            return [.. ReadAll().Where(purge => !purge.Dismissed).OrderBy(purge => purge.CreatedAt)];
    }

    /// <summary>
    /// 关闭显示。已完成或已放弃的任务删除记录；部分失败的只隐藏，失败的步骤照常重试，重试后重新显示。
    /// </summary>
    /// <returns>是否关闭；任务不存在或仍在等待、执行中时为 false。</returns>
    public bool Dismiss(string pluginId)
    {
        lock (_lock)
        {
            if (Read(pluginId) is not { } purge || purge.State is PluginPurgeState.Waiting or PluginPurgeState.Running) return false;
            if (purge.State is PluginPurgeState.Completed or PluginPurgeState.Cancelled) StateFile.Delete(PathOf(pluginId));
            else
            {
                purge.Dismissed = true;
                StateFile.Write(PathOf(pluginId), purge);
            }

            return true;
        }
    }

    /// <summary>删除前预览清除范围：声明的表、bucket 全名，以及机密文件与浏览器用户目录是否存在。</summary>
    public PluginDataPreview Preview(PluginManifest manifest) =>
        new()
        {
            Tables         = manifest.Tables,
            Buckets        = [.. manifest.BucketPurposes.Select(purpose => BucketNames.Plugin(_settings.BucketPrefix, purpose))],
            SecretsFile    = File.Exists(SecretsPath(manifest.Id)),
            BrowserProfile = Directory.Exists(_browser.ProfileDirectory(manifest.Id))
        };

    /// <summary>
    /// 匹配可能的残留表名的前缀（不区分大小写）：插件 ID 原样与 <c>-</c> 换成 <c>_</c> 两种写法，各接 <c>_</c> 或 <c>-</c>。
    /// </summary>
    public static IReadOnlyList<string> LeftoverPrefixes(string pluginId)
    {
        string underscored = pluginId.Replace('-', '_');
        return [.. new[] { underscored + "_", pluginId + "_", pluginId + "-", underscored + "-" }.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>名字是否像是插件的表：以 <see cref="LeftoverPrefixes" /> 之一开头且不是其他插件声明的表。</summary>
    public static bool IsPossibleLeftover(string table, IReadOnlyList<string> prefixes, IReadOnlySet<string> declaredByOthers) =>
        !declaredByOthers.Contains(table) && prefixes.Any(prefix => table.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public override void Dispose()
    {
        _health.Changed -= OnHealthChanged;
        _gate.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await _infrastructure.Ready.WaitAsync(stoppingToken)) return;
        await RunPendingAsync(stoppingToken);
    }

    /// <summary>执行全部未完成的任务；基础设施未就绪时直接返回。同一时间只执行一轮。</summary>
    internal async Task RunPendingAsync(CancellationToken cancellationToken)
    {
        if (!_infrastructure.Ready.IsCompletedSuccessfully || !await _infrastructure.Ready) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<PluginPurge> pending;
            lock (_lock)
                pending = [.. ReadAll().Where(purge => purge.State is not (PluginPurgeState.Completed or PluginPurgeState.Cancelled))];
            foreach (PluginPurge purge in pending)
                await RunAsync(purge, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "执行插件数据清除时出错");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunAsync(PluginPurge purge, CancellationToken cancellationToken)
    {
        if (PluginPackageScanner.Scan(_settings.PluginRoot).Any(package => package.Manifest?.Id == purge.PluginId))
        {
            _logger.LogWarning("插件 {PluginId} 已重新安装，放弃剩余的数据清除步骤，避免删除新插件的数据", purge.PluginId);
            Finish(purge, PluginPurgeState.Cancelled);
            return;
        }

        purge.State = PluginPurgeState.Running;
        Save(purge);
        _logger.LogInformation("开始清除插件 {PluginId} 的数据", purge.PluginId);

        List<PluginPurgeStep> failed = [];
        foreach (PluginPurgeStep step in purge.Steps.Where(step => !step.Done))
        {
            try
            {
                step.Summary = await ExecuteAsync(purge, step.Kind, cancellationToken);
                step.Done    = true;
                step.Error   = null;
                _logger.LogInformation("清除插件 {PluginId} 的{Step}：{Summary}", purge.PluginId, StepName(step.Kind), step.Summary);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                step.Error = exception.Message;
                failed.Add(step);
                _logger.LogError(exception, "清除插件 {PluginId} 的{Step}失败，下次启动重试", purge.PluginId, StepName(step.Kind));
            }

            Save(purge);
        }

        Finish(purge, failed.Count == 0 ? PluginPurgeState.Completed : PluginPurgeState.PartiallyFailed);
        if (failed.Count == 0)
        {
            _logger.LogInformation("插件 {PluginId} 的数据已清除", purge.PluginId);
            return;
        }

        Notify(
            new NotificationCard
            {
                Level  = NotificationLevel.Error,
                Code   = FAILED_CODE,
                Title  = "插件数据清除未完成",
                Cause  = $"{purge.Name}：{string.Join("、", failed.Select(step => StepName(step.Kind)))}失败",
                Impact = "失败的步骤在下次启动时重试，详见面板插件管理页与日志"
            });
    }

    private void Finish(PluginPurge purge, PluginPurgeState state)
    {
        purge.State      = state;
        purge.FinishedAt = _time.GetUtcNow();
        purge.Dismissed  = false;
        Save(purge);
    }

    private Task<string> ExecuteAsync(PluginPurge purge, PluginPurgeStepKind kind, CancellationToken cancellationToken) =>
        kind switch
        {
            PluginPurgeStepKind.Tables          => DropTablesAsync(purge, cancellationToken),
            PluginPurgeStepKind.SchemaVersions  => DeleteSchemaVersionsAsync(purge.PluginId, cancellationToken),
            PluginPurgeStepKind.MediaReferences => RemoveMediaReferencesAsync(purge.PluginId),
            PluginPurgeStepKind.Buckets         => DeleteBucketsAsync(purge.Scope.BucketPurposes, cancellationToken),
            PluginPurgeStepKind.ConfigAndState  => DeleteConfigAndStateAsync(purge.PluginId),
            PluginPurgeStepKind.Secrets         => Task.FromResult(DeleteSecrets(purge.PluginId)),
            PluginPurgeStepKind.BrowserProfile  => ClearBrowserProfileAsync(purge.PluginId, cancellationToken),
            _                                   => FindLeftoversAsync(purge, cancellationToken)
        };

    private async Task<string> DropTablesAsync(PluginPurge purge, CancellationToken cancellationToken)
    {
        PluginTables tables = purge.Scope.Tables;
        if (tables.Global.Length == 0 && tables.Account.Length == 0) return "没有声明数据表";

        ShinanoDatabase database = _infrastructure.Database;
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        int global = await DropAsync(connection, database.Schemas.Global, tables.Global, cancellationToken);
        IReadOnlyList<string> accounts = await AccountSchemasAsync(connection, database.Schemas, cancellationToken);
        int account = 0;
        foreach (string schema in accounts)
            account += await DropAsync(connection, schema, tables.Account, cancellationToken);
        return $"global 删除 {global} 张{SEPARATOR}账号 schema {accounts.Count} 个，删除 {account} 张";
    }

    // 只删除声明且存在的表；一条语句删除同一 schema 中的全部表，表之间的依赖不影响删除顺序。
    private static async Task<int> DropAsync(NpgsqlConnection connection, string schema, string[] declared, CancellationToken cancellationToken)
    {
        if (declared.Length == 0) return 0;

        List<string> existing = [];
        // SQL_NEED: 按 schema 查询目录中存在的表，SqlSugar 只能查询当前 schema。
        await using (NpgsqlCommand query = new("select table_name from information_schema.tables where table_schema = @schema and table_name = any(@names)", connection))
        {
            query.Parameters.AddWithValue("schema", schema);
            query.Parameters.AddWithValue("names", declared);
            await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) existing.Add(reader.GetString(0));
        }

        if (existing.Count == 0) return 0;

        string targets = string.Join(", ", existing.Select(table => $"\"{SchemaNames.RequireIdentifier(schema)}\".\"{SchemaNames.RequireIdentifier(table)}\""));
        // SQL_NEED: 删除表的 DDL；标识符经校验后拼入，不使用 cascade，避免连带删除其他插件依赖的对象。
        await using NpgsqlCommand drop = new($"drop table {targets}", connection);
        await drop.ExecuteNonQueryAsync(cancellationToken);
        return existing.Count;
    }

    // 包括已切换走的旧账号：插件数据在每个曾经使用过的账号 schema 中都可能存在。
    private static async Task<IReadOnlyList<string>> AccountSchemasAsync(NpgsqlConnection connection, SchemaNames schemas, CancellationToken cancellationToken)
    {
        List<string> result = [];
        // SQL_NEED: 枚举数据库中的 schema，SqlSugar 不提供。
        await using NpgsqlCommand query = new("select nspname from pg_namespace", connection);
        await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string name = reader.GetString(0);
            if (name.StartsWith(schemas.AccountPrefix, StringComparison.Ordinal)
                && long.TryParse(name.AsSpan(schemas.AccountPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out long selfId)
                && selfId > 0
                && schemas.Account(selfId) == name)
                result.Add(name);
        }

        return result;
    }

    private async Task<string> DeleteSchemaVersionsAsync(string pluginId, CancellationToken cancellationToken)
    {
        ShinanoDatabase database = _infrastructure.Database;
        List<ISqlSugarClient> clients = [database.Global];
        await using (NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken))
            foreach (string schema in await AccountSchemasAsync(connection, database.Schemas, cancellationToken))
                clients.Add(database.Account(long.Parse(schema.AsSpan(database.Schemas.AccountPrefix.Length), CultureInfo.InvariantCulture)));

        int deleted = 0;
        foreach (ISqlSugarClient client in clients.Where(client => client.DbMaintenance.IsAnyTable(SCHEMA_VERSION, false)))
            deleted += await client.Deleteable<SchemaVersionRecord>().Where(record => record.Owner == pluginId).ExecuteCommandAsync();
        return $"删除 {deleted} 条记录";
    }

    private async Task<string> RemoveMediaReferencesAsync(string pluginId)
    {
        int removed = await _infrastructure.PersistentMedia.RemovePluginReferencesAsync(pluginId);
        return $"删除 {removed} 条引用（不再被引用的媒体经宽限期后回收）";
    }

    private async Task<string> DeleteBucketsAsync(string[] purposes, CancellationToken cancellationToken)
    {
        if (purposes.Length == 0) return "没有声明 bucket";

        List<string> results = [];
        foreach (string purpose in purposes)
        {
            string bucket = BucketNames.Plugin(_settings.BucketPrefix, purpose);
            int? deleted = await _infrastructure.Storage.DeleteBucketAsync(bucket, cancellationToken);
            results.Add(deleted is { } count ? $"{bucket}：删除 {count} 个对象，已删除 bucket" : $"{bucket}：不存在");
        }

        return string.Join(SEPARATOR, results);
    }

    private async Task<string> DeleteConfigAndStateAsync(string pluginId)
    {
        ISqlSugarClient global = _infrastructure.Database.Global;
        int config = await global.Deleteable<PluginConfigRecord>().In(pluginId).ExecuteCommandAsync();
        int state = await global.Deleteable<PluginStateRecord>().In(pluginId).ExecuteCommandAsync();
        return $"删除配置 {config} 条，启用状态与顺序 {state} 条";
    }

    private string DeleteSecrets(string pluginId)
    {
        string path = SecretsPath(pluginId);
        if (!File.Exists(path)) return "不存在";
        File.Delete(path);
        return $"已删除 {PluginContext.SECRETS_DIRECTORY_NAME}/{pluginId}{FILE_EXTENSION}";
    }

    private async Task<string> ClearBrowserProfileAsync(string pluginId, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_browser.ProfileDirectory(pluginId))) return "不存在";
        await _browser.ClearProfileAsync(pluginId, cancellationToken);
        return "已删除";
    }

    private async Task<string> FindLeftoversAsync(PluginPurge purge, CancellationToken cancellationToken)
    {
        HashSet<string> declaredByOthers =
        [
            .. PluginPackageScanner.Scan(_settings.PluginRoot)
                                   .Select(package => package.Manifest)
                                   .OfType<PluginManifest>()
                                   .Where(manifest => manifest.Id != purge.PluginId)
                                   .SelectMany(manifest => manifest.Tables.Global.Concat(manifest.Tables.Account))
        ];
        IReadOnlyList<string> prefixes = LeftoverPrefixes(purge.PluginId);

        ShinanoDatabase database = _infrastructure.Database;
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        HashSet<string> schemas = [database.Schemas.Global, .. await AccountSchemasAsync(connection, database.Schemas, cancellationToken)];
        List<string> leftovers = [];
        // SQL_NEED: 跨 schema 列出全部表，SqlSugar 只能查询当前 schema。
        await using (NpgsqlCommand query = new("select table_schema, table_name from information_schema.tables where table_schema = any(@schemas) order by 1, 2", connection))
        {
            query.Parameters.AddWithValue("schemas", schemas.ToArray());
            await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                if (IsPossibleLeftover(reader.GetString(1), prefixes, declaredByOthers))
                    leftovers.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }

        purge.PossibleLeftovers = leftovers;
        if (leftovers.Count > 0)
            _logger.LogWarning("插件 {PluginId} 可能的残留表（未声明，未删除）：{Tables}", purge.PluginId, string.Join("、", leftovers));
        return leftovers.Count == 0 ? "未发现" : $"发现 {leftovers.Count} 个，未删除";
    }

    private static string StepName(PluginPurgeStepKind kind) =>
        kind switch
        {
            PluginPurgeStepKind.Tables          => "数据表",
            PluginPurgeStepKind.SchemaVersions  => "结构版本",
            PluginPurgeStepKind.MediaReferences => "持久媒体引用",
            PluginPurgeStepKind.Buckets         => "bucket",
            PluginPurgeStepKind.ConfigAndState  => "配置与状态",
            PluginPurgeStepKind.Secrets         => "机密",
            PluginPurgeStepKind.BrowserProfile  => "浏览器目录",
            _                                   => "可能的残留检查"
        };

    // 清除在启动阶段执行，此时机器人可能尚未连上；连上后再通知。
    private void Notify(NotificationCard card)
    {
        lock (_lock)
        {
            if (_bot.Api is null)
            {
                _deferredNotices.Add(card);
                return;
            }
        }

        _notifier.Notify(card);
    }

    private void OnHealthChanged(HealthChange change)
    {
        if (change.Current is not { Component: HealthComponent.Bot, Level: HealthLevel.Normal }) return;

        List<NotificationCard> notices;
        lock (_lock)
        {
            notices = [.. _deferredNotices];
            _deferredNotices.Clear();
        }

        foreach (NotificationCard notice in notices) _notifier.Notify(notice);
    }

    private string SecretsPath(string pluginId) =>
        Path.Combine(_settings.DataDirectory, PluginContext.SECRETS_DIRECTORY_NAME, pluginId + FILE_EXTENSION);

    private string PathOf(string pluginId) => Path.Combine(Root, pluginId + FILE_EXTENSION);

    private PluginPurge? Read(string pluginId) => StateFile.Read<PluginPurge>(PathOf(pluginId));

    private List<PluginPurge> ReadAll() =>
        [.. Directory.EnumerateFiles(Root, "*" + FILE_EXTENSION).Select(StateFile.Read<PluginPurge>).OfType<PluginPurge>()];

    private void Save(PluginPurge purge)
    {
        lock (_lock)
            StateFile.Write(PathOf(purge.PluginId), purge);
    }
}
