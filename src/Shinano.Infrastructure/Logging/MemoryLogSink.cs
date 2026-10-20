using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Serilog.Core;
using Serilog.Parsing;
using Serilog.Events;

namespace Shinano.Infrastructure.Logging;

/// <summary>
/// 保留最近日志的内存环形缓冲，并向实时订阅者推送新日志。
/// 写入不阻塞：订阅者处理过慢时丢弃其最早的未读日志。
/// </summary>
public sealed class MemoryLogSink : ILogEventSink
{
    /// <summary>环形缓冲保留的日志条数。</summary>
    public const int BUFFER_CAPACITY = 1000;

    private const int SUBSCRIBER_CAPACITY = 1000;

    private readonly Lock                     _lock        = new();
    private readonly Queue<LogEntry>          _buffer      = new(BUFFER_CAPACITY);
    private readonly List<Channel<LogEntry>> _subscribers = [];
    private          long                     _sequence;

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        string category = logEvent.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? value)
                          && value is ScalarValue { Value: string context }
            ? context
            : string.Empty;

        lock (_lock)
        {
            LogEntry entry = new()
            {
                Sequence  = ++_sequence,
                Timestamp = logEvent.Timestamp,
                Level     = logEvent.Level.ToString(),
                Category  = category,
                Message   = logEvent.RenderMessage(),
                Segments  = Split(logEvent),
                Exception = logEvent.Exception?.ToString()
            };
            if (_buffer.Count == BUFFER_CAPACITY) _buffer.Dequeue();
            _buffer.Enqueue(entry);
            foreach (Channel<LogEntry> subscriber in _subscribers)
                subscriber.Writer.TryWrite(entry);
        }
    }

    /// <summary>返回缓冲中的全部日志，按序号升序。</summary>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock)
        {
            return [.. _buffer];
        }
    }

    /// <summary>
    /// 先返回缓冲中的日志，再持续返回新日志，直到 <paramref name="cancellationToken" /> 取消。
    /// 取消时正常结束枚举。
    /// </summary>
    public async IAsyncEnumerable<LogEntry> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<LogEntry> channel = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(SUBSCRIBER_CAPACITY)
            {
                FullMode     = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        LogEntry[] backlog;
        lock (_lock)
        {
            backlog = [.. _buffer];
            _subscribers.Add(channel);
        }

        try
        {
            foreach (LogEntry entry in backlog)
                yield return entry;

            while (await WaitToReadAsync(channel.Reader, cancellationToken))
            {
                while (channel.Reader.TryRead(out LogEntry? entry))
                    yield return entry;
            }
        }
        finally
        {
            lock (_lock)
            {
                _subscribers.Remove(channel);
            }
        }
    }

    private static async ValueTask<bool> WaitToReadAsync(
        ChannelReader<LogEntry> reader,
        CancellationToken       cancellationToken)
    {
        try
        {
            return await reader.WaitToReadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    // 与 RenderMessage 相同的渲染方式逐个渲染模板记号，参数按值类型标注种类。
    private static List<LogSegment> Split(LogEvent logEvent)
    {
        List<LogSegment> segments = [];
        foreach (MessageTemplateToken token in logEvent.MessageTemplate.Tokens)
        {
            using StringWriter writer = new();
            token.Render(logEvent.Properties, writer);
            LogSegmentKind kind = token is PropertyToken property
                                  && logEvent.Properties.TryGetValue(property.PropertyName, out LogEventPropertyValue? value)
                ? KindOf(value)
                : LogSegmentKind.Text;
            segments.Add(
                new LogSegment
                {
                    Text = writer.ToString(),
                    Kind = kind
                });
        }

        return segments;
    }

    private static LogSegmentKind KindOf(LogEventPropertyValue value) =>
        value switch
        {
            ScalarValue { Value: null }   => LogSegmentKind.Null,
            ScalarValue { Value: string } => LogSegmentKind.String,
            ScalarValue { Value: bool }   => LogSegmentKind.Boolean,
            ScalarValue { Value: byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal }
                => LogSegmentKind.Number,
            _ => LogSegmentKind.Other
        };
}
