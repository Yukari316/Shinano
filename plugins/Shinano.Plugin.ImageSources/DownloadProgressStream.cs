using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Shinano.Plugin.ImageSources;

/// <summary>
/// 只读的下载流包装：读取期间每秒记录一次进度（已下载、总大小与百分比）与这一秒的速度，读完时记录总量与平均速度。
/// 不缓冲内容，读取由下游（如写入 bucket）驱动。不是线程安全的，与被包装的流一样只能顺序读取。
/// </summary>
public sealed class DownloadProgressStream : Stream
{
    private const long PERCENT = 100;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly Stream    _inner;
    private readonly long?     _length;
    private readonly string    _label;
    private readonly ILogger   _logger;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private          long      _read;
    private          long      _readAtLastLog;
    private          TimeSpan  _lastLog;
    private          bool      _completed;

    /// <summary>包装下载流。</summary>
    /// <param name="inner">被包装的流，随本流释放。</param>
    /// <param name="length">声明的总大小，单位字节；未知时为 null。</param>
    /// <param name="label">日志中的下载对象说明，如「Pixiv 作品 123 第 0 页」。</param>
    /// <param name="logger">日志。</param>
    public DownloadProgressStream(Stream inner, long? length, string label, ILogger logger)
    {
        _inner  = inner;
        _length = length;
        _label  = label;
        _logger = logger;
    }

    /// <summary>已读取的字节数。</summary>
    public long BytesRead => _read;

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => Track(_inner.Read(buffer, offset, count));

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Track(await _inner.ReadAsync(buffer, cancellationToken));

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        await base.DisposeAsync();
    }

    private int Track(int count)
    {
        _read += count;
        TimeSpan now = _elapsed.Elapsed;
        if (count == 0 && !_completed)
        {
            _completed = true;
            _logger.LogInformation(
                "下载{Label}完成：{Size}，用时 {Seconds:0.#} 秒，平均 {Speed}/s",
                _label,
                ImageInspector.FormatSize(_read),
                now.TotalSeconds,
                ImageInspector.FormatSize(Speed(_read, now)));
        }
        else if (now - _lastLog >= Interval)
        {
            _logger.LogInformation(
                "下载{Label}：{Read} / {Total}{Percent}，{Speed}/s",
                _label,
                ImageInspector.FormatSize(_read),
                ImageInspector.FormatSize(_length),
                _length is long total and > 0 ? $"（{_read * PERCENT / total}%）" : string.Empty,
                ImageInspector.FormatSize(Speed(_read - _readAtLastLog, now - _lastLog)));
            _lastLog       = now;
            _readAtLastLog = _read;
        }

        return count;
    }

    private static long Speed(long bytes, TimeSpan elapsed) =>
        elapsed > TimeSpan.Zero ? (long)(bytes / elapsed.TotalSeconds) : bytes;
}
