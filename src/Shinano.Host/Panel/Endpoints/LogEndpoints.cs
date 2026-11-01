using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Shinano.Infrastructure.Logging;
using Shinano.Infrastructure.Logging.Models;

namespace Shinano.Host.Panel.Endpoints;

/// <summary>日志：实时日志以 SSE 推送内存日志流，历史日志从 <c>Logs</c> 目录的文件读取。</summary>
internal static class LogEndpoints
{
    private const int DEFAULT_TAIL_LINES = 500;
    private const int MAX_TAIL_LINES     = 5000;


    // 先推送缓冲中的日志再持续推送新日志，连接断开时结束。
    internal static async Task StreamAsync(
        HttpContext                context,
        MemoryLogSink              sink,
        IOptions<JsonOptions>      jsonOptions,
        CancellationToken          cancellationToken)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.Body.FlushAsync(cancellationToken);
        await foreach (LogEntry entry in sink.SubscribeAsync(cancellationToken))
        {
            string json = JsonSerializer.Serialize(entry, jsonOptions.Value.SerializerOptions);
            await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
    }

    internal static IResult ListFiles(LogFileLayout layout) =>
        Results.Ok(
            new DirectoryInfo(layout.Directory)
                .EnumerateFiles("*" + LogFileLayout.FILE_EXTENSION)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => new
                {
                    name     = file.Name,
                    size     = file.Length,
                    modified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)
                }));

    // 读取文件末尾若干行；文件名只接受 Logs 目录内的日志文件名，不接受路径。
    internal static async Task<IResult> ReadFileAsync(
        string            name,
        LogFileLayout     layout,
        CancellationToken cancellationToken,
        int               lines = DEFAULT_TAIL_LINES)
    {
        if (Path.GetFileName(name) != name || !name.EndsWith(LogFileLayout.FILE_EXTENSION, StringComparison.Ordinal))
            return PanelResults.Error(StatusCodes.Status400BadRequest, "日志文件名无效");

        string path = Path.Combine(layout.Directory, name);
        if (!File.Exists(path)) return Results.NotFound();

        // 当前进程正在写入该文件，以共享读写方式打开。
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, Encoding.UTF8);
        Queue<string> tail = new();
        int limit = Math.Clamp(lines, 1, MAX_TAIL_LINES);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (tail.Count == limit) tail.Dequeue();
            tail.Enqueue(line);
        }

        return Results.Ok(new { name, lines = tail });
    }
}
