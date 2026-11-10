using Microsoft.Extensions.Logging;
using Npgsql;
using Shinano.Host.Bot.Models;
using Shinano.Infrastructure.ChatArchive;
using Shinano.Infrastructure.ChatArchive.Models;
using Shinano.Infrastructure.Hosting;
using Sora.Entities.Interfaces;
using Sora.Entities.Recording;
using System.Threading.Channels;

namespace Shinano.Host.Bot;

/// <summary>
/// 宿主唯一的消息记录器与归档工作者。Sora 在记录队列上调用 <see cref="OnMessageAsync" />，
/// 这里只把记录写入有界的归档缓冲后立即返回；缓冲已满时丢弃新记录，并登记归档积压缺口、通知 Super User。
/// 单个工作者顺序写入正文与统计，需要下载媒体或展开转发的消息交给后台媒体工作者。
/// </summary>
/// <remarks>
/// 进程停止时先由 Sora 排空记录队列，再排空归档缓冲；未完成的媒体归档保持待处理标记，下次启动时标记为缺失。
/// 须在 <c>BotService</c> 之前登记为托管服务，使其在 Sora 停止之后才停止。
/// </remarks>
public sealed class ChatArchiveService : BackgroundService, IMessageRecorder
{
    private const int MEDIA_QUEUE_CAPACITY = 1000;
    private const int MEDIA_WORKER_COUNT   = 4;

    private static readonly TimeSpan GapFlushInterval = TimeSpan.FromMinutes(1);

    private readonly HostInfrastructure          _infrastructure;
    private readonly HostScheduler               _scheduler;
    private readonly BotAccessor                 _bot;
    private readonly ArchiveGapTracker           _gaps;
    private readonly SuperUserNotifier           _notifier;
    private readonly TimeProvider                _time;
    private readonly ILogger<ChatArchiveService> _logger;
    private readonly Channel<MessageRecord>      _records;
    private readonly Channel<ChatArchiveEntry>   _mediaJobs;
    private volatile bool                        _stopping;

    /// <summary>创建服务。</summary>
    public ChatArchiveService(
        HostInfrastructure          infrastructure,
        ChatArchiveOptions          options,
        HostScheduler               scheduler,
        BotAccessor                 bot,
        ArchiveGapTracker           gaps,
        SuperUserNotifier           notifier,
        TimeProvider                time,
        ILogger<ChatArchiveService> logger)
    {
        _infrastructure = infrastructure;
        _scheduler      = scheduler;
        _bot            = bot;
        _gaps           = gaps;
        _notifier       = notifier;
        _time           = time;
        _logger         = logger;
        _records = Channel.CreateBounded<MessageRecord>(
            new BoundedChannelOptions(options.BufferCapacity)
            {
                SingleReader = true
            });
        _mediaJobs = Channel.CreateBounded<ChatArchiveEntry>(
            new BoundedChannelOptions(MEDIA_QUEUE_CAPACITY)
            {
                SingleWriter = true
            });
    }

    /// <inheritdoc />
    public ValueTask OnMessageAsync(MessageRecord record, CancellationToken ct)
    {
        if (_stopping) return ValueTask.CompletedTask;

        DateTimeOffset now = _time.GetUtcNow();
        if (_records.Writer.TryWrite(record))
        {
            if (_gaps.Close(ArchiveGapCause.Backlog, now))
            {
                _logger.LogInformation("归档缓冲已恢复写入");
                _notifier.Notify(
                    new NotificationCard
                    {
                        Level  = NotificationLevel.Normal,
                        Code   = "ARCHIVE-RECOVERED",
                        Title  = "聊天归档缓冲已恢复",
                        Impact = "已恢复归档"
                    });
            }
        }
        else if (_gaps.Open(ArchiveGapCause.Backlog, now))
        {
            _logger.LogWarning("归档缓冲已满，开始丢弃消息记录");
            _notifier.Notify(
                new NotificationCard
                {
                    Level  = NotificationLevel.Warning,
                    Code   = "ARCHIVE-BACKLOG",
                    Title  = "聊天归档缓冲已满",
                    Cause  = "归档处理跟不上消息量",
                    Impact = "新消息暂不归档，登记为归档缺口"
                });
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        _records.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
        _gaps.CloseAll(_time.GetUtcNow());
        await FlushGapsAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await _infrastructure.Ready.WaitAsync(stoppingToken)) return;

        _scheduler.Register(
            HostInfrastructure.SCHEDULER_OWNER,
            "archive-gaps",
            GapFlushInterval,
            FlushGapsAsync,
            runImmediately: false);
        // 不传递父流程的执行上下文：SqlSugarScope 以 AsyncLocal 分配客户端，继承后工作任务会与父流程共用同一个数据库连接。
        Task[] mediaWorkers;
        using (ExecutionContext.SuppressFlow())
            mediaWorkers = [.. Enumerable.Range(0, MEDIA_WORKER_COUNT).Select(_ => Task.Run(() => RunMediaWorkerAsync(stoppingToken)))];

        // 停止时继续排空缓冲，直到写入端在 StopAsync 中关闭；等待时间受宿主停止超时限制。
        await foreach (MessageRecord record in _records.Reader.ReadAllAsync(CancellationToken.None))
            await ArchiveAsync(record);

        _mediaJobs.Writer.TryComplete();
        await Task.WhenAll(mediaWorkers);
    }

    private async Task ArchiveAsync(MessageRecord record)
    {
        long account = _bot.SelfId;
        if (account != 0 && record.SelfId != account)
        {
            _logger.LogWarning("消息记录的机器人账号 {SelfId} 与当前账号 {Account} 不一致，未归档", (long)record.SelfId, account);
            return;
        }

        // 数据库离线期间丢弃记录，该区间已由健康变化登记为归档缺口。
        if (!_infrastructure.DatabaseAvailability.IsAvailable) return;

        try
        {
            ChatArchiveEntry? entry = ChatRecordConverter.Convert(record, _infrastructure.TimeZone);
            if (entry is null)
            {
                _logger.LogWarning("不支持归档来源为 {SourceType} 的消息", record.SourceType);
                return;
            }

            await _infrastructure.InitializeAccountAsync(entry.SelfId, CancellationToken.None);
            long? rowId = await _infrastructure.ChatArchive.InsertAsync(entry.SelfId, entry.Message, CancellationToken.None);
            if (rowId is null || !entry.HasPendingWork) return;

            entry.RowId = rowId.Value;
            if (_mediaJobs.Writer.TryWrite(entry)) return;

            _logger.LogWarning("媒体归档队列已满，消息 {MessageId} 的媒体标记为缺失", entry.Message.MessageId);
            await _infrastructure.ChatArchive.UpdateSegmentsAsync(
                entry.SelfId,
                entry.RowId,
                entry.Message.SentAt,
                entry.Message.Segments,
                ChatArchiveFlags.ContentMissing,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "归档消息 {MessageId} 失败", (long)record.MessageId);
            await ProbeDatabaseAsync(exception);
        }
    }

    private async Task RunMediaWorkerAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (ChatArchiveEntry entry in _mediaJobs.Reader.ReadAllAsync(stoppingToken))
                await ArchiveMediaAsync(entry, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // 停止时放弃剩余任务，它们保持待处理标记，下次启动时标记为缺失。
        }
    }

    private async Task ArchiveMediaAsync(ChatArchiveEntry entry, CancellationToken stoppingToken)
    {
        try
        {
            IBotApi? api = _bot.Api;
            ChatArchiveFlags flags = await _infrastructure.ChatMediaArchiver.ArchiveAsync(entry, api, stoppingToken);
            if (flags.HasFlag(ChatArchiveFlags.ContentMissing))
                _logger.LogWarning("消息 {MessageId} 有媒体或转发内容未能归档", entry.Message.MessageId);
            await _infrastructure.ChatArchive.UpdateSegmentsAsync(
                entry.SelfId,
                entry.RowId,
                entry.Message.SentAt,
                entry.Message.Segments,
                flags,
                stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "归档消息 {MessageId} 的媒体失败", entry.Message.MessageId);
            await ProbeDatabaseAsync(exception);
        }
    }

    // SqlSugar 把连接失败包装为不含内层异常的 SqlSugarException，无法按类型区分；
    // 服务端已响应的错误说明数据库在线，其余失败立即强制探测，离线时由健康变化登记缺口，之后的记录直接丢弃。
    private async Task ProbeDatabaseAsync(Exception exception)
    {
        if (exception is PostgresException) return;

        await _infrastructure.DatabaseAvailability.CheckAsync(force: true, CancellationToken.None);
    }

    private async Task FlushGapsAsync(CancellationToken cancellationToken)
    {
        long account = _bot.SelfId;
        if (account == 0) return;

        IReadOnlyList<ArchiveGap> gaps = _gaps.TakeClosed();
        if (gaps.Count == 0) return;

        try
        {
            await _infrastructure.ChatArchive.WriteGapsAsync(account, gaps, cancellationToken);
            _logger.LogInformation("已登记 {Count} 段归档缺口", gaps.Count);
        }
        catch (Exception exception)
        {
            _gaps.Requeue(gaps);
            _logger.LogError(exception, "登记归档缺口失败，将在下次重试");
        }
    }
}
