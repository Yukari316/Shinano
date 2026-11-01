using Microsoft.Extensions.Logging;
using Shinano.Infrastructure.ChatArchive;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Health.Models;
using Shinano.Infrastructure.Hosting.Models;
using Shinano.Infrastructure.Media;
using Shinano.Infrastructure.Media.Entities;
using Shinano.Infrastructure.Storage;

namespace Shinano.Infrastructure.Hosting;

/// <summary>
/// 宿主基础设施的启动与运行入口：启动检查数据库与 RustFS，创建宿主 schema、表与 bucket，
/// 登记宿主维护任务。任一检查失败时进入故障模式（启动组成部分为停止），Bot 不上线，管理面板继续可用。
/// </summary>
public sealed class HostInfrastructure : IDisposable
{
    /// <summary>宿主在调度器中的所有者名。</summary>
    public const string SCHEDULER_OWNER = "host";

    private static readonly TimeSpan MaintenanceInterval   = TimeSpan.FromHours(1);
    private static readonly TimeSpan RecoveryProbeInterval = TimeSpan.FromSeconds(30);

    private readonly HostInfrastructureSettings  _settings;
    private readonly RuntimeHealth               _health;
    private readonly HostScheduler               _scheduler;
    private readonly TimeProvider                _time;
    private readonly ILoggerFactory              _loggerFactory;
    private readonly ILogger<HostInfrastructure> _logger;
    private readonly TaskCompletionSource<bool>  _ready               = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<long>               _initializedAccounts = [];
    private readonly SemaphoreSlim               _accountGate         = new(1, 1);
    private          ShinanoDatabase?            _database;
    private          ObjectStorage?              _storage;
    private          HttpClient?                 _httpClient;
    private          Components?                 _components;

    /// <summary>创建入口，不立即连接任何服务。</summary>
    public HostInfrastructure(
        HostInfrastructureSettings settings,
        RuntimeHealth              health,
        HostScheduler              scheduler,
        TimeProvider               time,
        ILoggerFactory             loggerFactory)
    {
        _settings      = settings;
        _health        = health;
        _scheduler     = scheduler;
        _time          = time;
        _loggerFactory = loggerFactory;
        _logger        = loggerFactory.CreateLogger<HostInfrastructure>();
    }

    /// <summary>启动检查完成后得到结果：true 为就绪，false 为故障模式。</summary>
    public Task<bool> Ready => _ready.Task;

    /// <summary>配置的自然日时区，就绪后可用。</summary>
    public TimeZoneInfo TimeZone => RequireReady().TimeZone;

    /// <summary>数据库入口，就绪后可用。</summary>
    public ShinanoDatabase Database => RequireReady().Database;

    /// <summary>对象存储，就绪后可用。</summary>
    public ObjectStorage Storage => RequireReady().Storage;

    /// <summary>持久媒体存储，就绪后可用。</summary>
    public PersistentMediaStore PersistentMedia => RequireReady().PersistentMedia;

    /// <summary>聊天媒体存储，就绪后可用。</summary>
    public ChatMediaStore ChatMedia => RequireReady().ChatMedia;

    /// <summary>媒体下载器，就绪后可用。</summary>
    public MediaDownloader Downloader => RequireReady().Downloader;

    /// <summary>聊天记录、统计与缺口存储，就绪后可用。</summary>
    public ChatArchiveStore ChatArchive => RequireReady().ChatArchive;

    /// <summary>聊天媒体与合并转发的后台归档，就绪后可用。</summary>
    public ChatMediaArchiver ChatMediaArchiver => RequireReady().ChatMediaArchiver;

    /// <summary>数据库运行中可用性，就绪后可用。</summary>
    public DependencyAvailability DatabaseAvailability => RequireReady().DatabaseAvailability;

    /// <summary>RustFS 运行中可用性，就绪后可用。</summary>
    public DependencyAvailability StorageAvailability => RequireReady().StorageAvailability;

    /// <summary>
    /// 执行启动检查。每个进程只调用一次；失败不抛出异常，而是记录原因并进入故障模式。
    /// </summary>
    /// <returns>是否就绪。</returns>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _components = await CreateComponentsAsync(cancellationToken);
            RegisterMaintenance(_components);
            _logger.LogInformation("基础设施启动检查通过");
            _ready.TrySetResult(true);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "基础设施启动检查失败，进入故障模式");
            _health.Set(HealthComponent.Startup, HealthLevel.Stopped, exception.Message);
            _ready.TrySetResult(false);
            return false;
        }
        catch (OperationCanceledException)
        {
            _ready.TrySetResult(false);
            throw;
        }
    }

    /// <summary>
    /// 初始化机器人账号的 schema、聊天记录相关表与分区，把上次进程未完成的媒体归档标记为缺失，
    /// 并登记该账号的维护任务（分区、统计清理）。同一账号重复调用直接返回。
    /// </summary>
    public async Task InitializeAccountAsync(long selfId, CancellationToken cancellationToken)
    {
        Components components = RequireReady();
        await _accountGate.WaitAsync(cancellationToken);
        try
        {
            if (_initializedAccounts.Contains(selfId)) return;

            await components.Database.EnsureSchemaAsync(components.Database.Schemas.Account(selfId), cancellationToken);
            await new SchemaMigrator(_loggerFactory.CreateLogger<SchemaMigrator>()).ApplyAsync(
                components.Database.Account(selfId),
                SchemaMigrator.HOST_OWNER,
                [],
                cancellationToken);
            await components.ChatArchive.EnsureTablesAsync(selfId, cancellationToken);
            int interrupted = await components.ChatArchive.MarkInterruptedAsync(selfId, cancellationToken);
            if (interrupted > 0)
                _logger.LogWarning("上次运行有 {Count} 条消息的媒体未归档完成，已标记为缺失", interrupted);
            _scheduler.Register(
                SCHEDULER_OWNER,
                $"chat-archive-{selfId}",
                MaintenanceInterval,
                token => MaintainAccountAsync(components, selfId, token),
                runImmediately: false);
            await MaintainAccountAsync(components, selfId, cancellationToken);
            _initializedAccounts.Add(selfId);
            _logger.LogInformation("机器人账号 {SelfId} 的数据结构已就绪", selfId);
        }
        finally
        {
            _accountGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _database?.Dispose();
        _storage?.Dispose();
        _httpClient?.Dispose();
    }

    private async Task<Components> CreateComponentsAsync(CancellationToken cancellationToken)
    {
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(_settings.Deployment.TimeZone);

        _database = new ShinanoDatabase(PostgresConnectionString.Parse(_settings.Database.Dsn), _settings.Schemas);
        int major = await _database.GetServerMajorVersionAsync(cancellationToken);
        if (major < ShinanoDatabase.MINIMUM_SERVER_MAJOR_VERSION)
            throw new InvalidOperationException(
                $"PostgreSQL 版本 {major} 低于要求的 {ShinanoDatabase.MINIMUM_SERVER_MAJOR_VERSION}");
        await _database.EnsureSchemaAsync(_database.Schemas.Global, cancellationToken);
        _database.Global.CodeFirst.InitTables<MediaObjectRecord, MediaReferenceRecord, ChatMediaObjectRecord>();
        await new SchemaMigrator(_loggerFactory.CreateLogger<SchemaMigrator>()).ApplyAsync(
            _database.Global,
            SchemaMigrator.HOST_OWNER,
            [],
            cancellationToken);

        _storage = new ObjectStorage(_settings.RustFs);
        await _storage.CheckAsync(cancellationToken);
        string chatBucket  = BucketNames.Chat(_settings.Deployment.BucketPrefix);
        string mediaBucket = BucketNames.Media(_settings.Deployment.BucketPrefix);
        await _storage.EnsureBucketAsync(chatBucket, cancellationToken);
        await _storage.EnsureBucketAsync(mediaBucket, cancellationToken);

        _httpClient = new HttpClient();
        ShinanoDatabase database = _database;
        ObjectStorage   storage  = _storage;
        ChatMediaStore chatMedia = new(database, storage, chatBucket, _loggerFactory.CreateLogger<ChatMediaStore>());
        MediaDownloader downloader = new(_httpClient, _settings.Media);
        DependencyAvailability storageAvailability = new(
            HealthComponent.ObjectStorage,
            HealthLevel.Degraded,
            storage.CheckAsync,
            RecoveryProbeInterval,
            _health,
            _time,
            _logger);
        return new Components
        {
            TimeZone        = timeZone,
            Database        = database,
            Storage         = storage,
            PersistentMedia = new PersistentMediaStore(
                database,
                storage,
                mediaBucket,
                _settings.Media,
                _loggerFactory.CreateLogger<PersistentMediaStore>()),
            ChatMedia       = chatMedia,
            Downloader      = downloader,
            ChatArchive     = new ChatArchiveStore(database),
            ChatMediaArchiver = new ChatMediaArchiver(
                chatMedia,
                downloader,
                storageAvailability,
                _loggerFactory.CreateLogger<ChatMediaArchiver>()),
            Partitions      = new ChatPartitionMaintainer(database, _loggerFactory.CreateLogger<ChatPartitionMaintainer>()),
            DatabaseAvailability = new DependencyAvailability(
                HealthComponent.Database,
                HealthLevel.Stopped,
                async token => await database.GetServerMajorVersionAsync(token),
                RecoveryProbeInterval,
                _health,
                _time,
                _logger),
            StorageAvailability = storageAvailability
        };
    }

    // 启动时先执行一次再按间隔执行，与日志清理一致：调试或运行不满一个间隔的进程同样会清理过期媒体，
    // 否则聊天分区已在账号初始化时删除，对应的聊天媒体记录与对象却一直残留。
    private void RegisterMaintenance(Components components)
    {
        _scheduler.Register(
            SCHEDULER_OWNER,
            "persistent-media-gc",
            MaintenanceInterval,
            token => components.PersistentMedia.CollectOrphansAsync(_time.GetUtcNow(), token),
            runImmediately: true);
        _scheduler.Register(
            SCHEDULER_OWNER,
            "chat-media-expiry",
            MaintenanceInterval,
            token => components.ChatMedia.ExpireAsync(
                _time.GetUtcNow() - TimeSpan.FromDays(_settings.ChatArchive.RetentionDays),
                token),
            runImmediately: true);
    }

    private async Task MaintainAccountAsync(Components components, long selfId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        await components.Partitions.EnsurePartitionsAsync(
            selfId,
            now,
            components.TimeZone,
            _settings.ChatArchive.PartitionDaysAhead,
            cancellationToken);
        int dropped = await components.Partitions.DropExpiredAsync(
            selfId,
            now,
            components.TimeZone,
            _settings.ChatArchive.RetentionDays,
            cancellationToken);
        _logger.LogInformation(
            "账号 {SelfId} 聊天记录过期清理（保留 {Days} 天）：数据库删除 {Count} 个聊天分区",
            selfId,
            _settings.ChatArchive.RetentionDays,
            dropped);
        if (_settings.ChatArchive.StatRetentionDays > 0)
        {
            DateOnly cutoff = ChatPartitionMaintainer.LocalDate(now, components.TimeZone)
                                                     .AddDays(-_settings.ChatArchive.StatRetentionDays);
            int deleted = await components.ChatArchive.DeleteStatsBeforeAsync(selfId, cutoff, cancellationToken);
            _logger.LogInformation("账号 {SelfId} 发言统计过期清理（早于 {Cutoff}）：数据库删除 {Count} 行", selfId, cutoff, deleted);
        }
    }

    private Components RequireReady() =>
        _components ?? throw new InvalidOperationException("基础设施尚未就绪");

    private sealed class Components
    {
        public required TimeZoneInfo            TimeZone             { get; init; }
        public required ShinanoDatabase         Database             { get; init; }
        public required ObjectStorage           Storage              { get; init; }
        public required PersistentMediaStore    PersistentMedia      { get; init; }
        public required ChatMediaStore          ChatMedia            { get; init; }
        public required MediaDownloader         Downloader           { get; init; }
        public required ChatArchiveStore        ChatArchive          { get; init; }
        public required ChatMediaArchiver       ChatMediaArchiver    { get; init; }
        public required ChatPartitionMaintainer Partitions           { get; init; }
        public required DependencyAvailability  DatabaseAvailability { get; init; }
        public required DependencyAvailability  StorageAvailability  { get; init; }
    }
}
