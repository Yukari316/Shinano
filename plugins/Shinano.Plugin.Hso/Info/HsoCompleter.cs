using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Hso.Gallery;
using Shinano.Plugin.Hso.Persistence;
using Shinano.Plugin.ImageSources;

namespace Shinano.Plugin.Hso.Info;

/// <summary>
/// 在后台补全图片的作品信息，不阻塞发图：抽到待补全的图片后由 <see cref="Enqueue" /> 触发，另有每小时一次的
/// <see cref="RunDailyAsync" /> 按配置每天补全若干张。查到作品信息后下载来源原图并比较 SHA256：一致视为同一张图，信息直接确认；
/// 不一致或原图下载失败时进入审核队列（<see cref="HsoReviews" />）；查无结果记为查无信息；查询失败保持待补全，之后再查。
/// 补全完成后不补发信息卡。同一张图片同时只补全一次。线程安全。
/// </summary>
public sealed class HsoCompleter
{
    /// <summary>每日补全任务的触发间隔：每天的数量分摊到每小时。</summary>
    public static readonly TimeSpan DailyJobInterval = TimeSpan.FromHours(1);

    private const int RUNS_PER_DAY = 24;

    private readonly IPluginInitializationContext _context;
    private readonly HsoGallery                   _gallery;
    private readonly HsoInfoResolver              _info;
    private readonly HsoOriginals                 _originals;
    private readonly HsoReviews                   _reviews;
    private readonly TimeProvider                 _time;
    private readonly ILogger<HsoCompleter>        _logger;
    private readonly Lock                         _lock    = new();
    private readonly HashSet<long>                _running = [];

    // 只由每日任务访问；调度器不会并发执行同一任务。重启后从 0 计，当天可能多补全几张。
    private DateOnly _day;
    private int      _completedToday;

    /// <summary>创建补全器。</summary>
    public HsoCompleter(
        IPluginInitializationContext context,
        HsoGallery                   gallery,
        HsoInfoResolver              info,
        HsoOriginals                 originals,
        HsoReviews                   reviews,
        TimeProvider                 time,
        ILogger<HsoCompleter>        logger)
    {
        _context   = context;
        _gallery   = gallery;
        _info      = info;
        _originals = originals;
        _reviews   = reviews;
        _time      = time;
        _logger    = logger;
    }

    /// <summary>在后台补全一张图片；它正在补全时忽略。异常只记录日志。</summary>
    public void Enqueue(long imageId)
    {
        if (!TryStart(imageId)) return;
        // 不传递调用方（抽图指令）的执行上下文：SqlSugarScope 以 AsyncLocal 为每个异步流程分配客户端，
        // 继承后补全会与指令共用同一个数据库连接并发执行命令（Npgsql 报 A command is already in progress）。
        using (ExecutionContext.SuppressFlow())
            _ = Task.Run(async () =>
            {
                try
                {
                    await CompleteAsync(imageId, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "后台补全 Hso 图片 {Id} 的作品信息失败", imageId);
                }
                finally
                {
                    Finish(imageId);
                }
            });
    }

    /// <summary>每日补全任务：把每天的数量分摊到每小时，补全最早导入的待补全图片。</summary>
    public async Task RunDailyAsync(CancellationToken cancellationToken)
    {
        int limit = _context.Config<HsoConfig>().Value.DailyCompletions;
        DateOnly today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        if (today != _day)
        {
            _day            = today;
            _completedToday = 0;
        }

        int count = Math.Min((limit + RUNS_PER_DAY - 1) / RUNS_PER_DAY, limit - _completedToday);
        if (count <= 0) return;

        List<long> ids = await _gallery.PendingIdsAsync(count);
        foreach (long id in ids)
        {
            if (!TryStart(id)) continue;
            try
            {
                await CompleteAsync(id, cancellationToken);
            }
            finally
            {
                Finish(id);
            }

            _completedToday++;
        }

        _logger.LogInformation("Hso 每日补全：本次补全 {Count} 张，今天共 {Total} / {Limit} 张", ids.Count, _completedToday, limit);
    }

    private async Task CompleteAsync(long imageId, CancellationToken cancellationToken)
    {
        // 重新读取：图片可能已被删除，或已由另一次补全或管理员处理。
        if (await _gallery.FindAsync(imageId) is not { InfoState: (int)HsoInfoState.Pending } record) return;

        byte[]? content = record.Pid is null ? await _gallery.ReadAsync(record, cancellationToken) : null;
        HsoLookup lookup = await _info.LookupAsync(record, content, cancellationToken);
        if (lookup is not { Status: HsoLookupStatus.Found, Info: { } info })
        {
            await _gallery.ApplyLookupAsync(record, lookup);
            return;
        }

        if (info.Work is not { } work)
        {
            await _reviews.EnqueueAsync(record, info, null, "没有可下载原图的来源", cancellationToken);
            return;
        }

        HsoDownload download = await _originals.DownloadAsync(work, cancellationToken);
        if (download.Content is not { } original)
        {
            await _reviews.EnqueueAsync(record, info, null, download.Error, cancellationToken);
            return;
        }

        string sha256 = ImageInspector.Sha256(original);
        if (sha256 == record.Sha256)
        {
            _logger.LogInformation("Hso 图片 {Id} 与来源 {Site} {WorkId} 的原图 SHA256 一致，作品信息直接确认", record.Id, work.Site, work.Id);
            await _gallery.ApplyLookupAsync(record, lookup);
            return;
        }

        if (HsoImageFiles.Detect(original) is not { } format)
        {
            await _reviews.EnqueueAsync(record, info, null, "来源原图不是 JPEG、PNG、GIF 或 WebP", cancellationToken);
            return;
        }

        await _reviews.EnqueueAsync(record, info, (original, format), null, cancellationToken);
    }

    private bool TryStart(long imageId)
    {
        lock (_lock) return _running.Add(imageId);
    }

    private void Finish(long imageId)
    {
        lock (_lock) _running.Remove(imageId);
    }
}
