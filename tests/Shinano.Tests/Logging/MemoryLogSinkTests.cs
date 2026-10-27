using Serilog.Events;
using Serilog.Parsing;
using Shinano.Infrastructure.Logging;
using Shinano.Infrastructure.Logging.Models;

namespace Shinano.Tests.Logging;

public sealed class MemoryLogSinkTests
{
    [Fact]
    public void Buffer_KeepsOnlyLatestEntries()
    {
        MemoryLogSink sink = new();
        for (int i = 0; i < MemoryLogSink.BUFFER_CAPACITY + 5; i++)
            sink.Emit(CreateEvent($"m{i}"));

        IReadOnlyList<LogEntry> snapshot = sink.Snapshot();

        Assert.Equal(MemoryLogSink.BUFFER_CAPACITY, snapshot.Count);
        Assert.Equal("m5", snapshot[0].Message);
        Assert.Equal($"m{MemoryLogSink.BUFFER_CAPACITY + 4}", snapshot[^1].Message);
    }

    [Fact]
    public async Task Subscribe_ReturnsBacklogThenNewEntries()
    {
        MemoryLogSink sink = new();
        sink.Emit(CreateEvent("before"));
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(5));
        List<string> received = [];

        Task reader = Task.Run(async () =>
        {
            await foreach (LogEntry entry in sink.SubscribeAsync(cancellation.Token))
            {
                received.Add(entry.Message);
                if (received.Count == 2) break;
            }
        }, cancellation.Token);
        while (!reader.IsCompleted && received.Count == 0)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        sink.Emit(CreateEvent("after"));
        await reader;

        Assert.Equal(["before", "after"], received);
    }

    private static LogEvent CreateEvent(string text) =>
        new(DateTimeOffset.Now, LogEventLevel.Information, null, new MessageTemplateParser().Parse(text), []);
}
