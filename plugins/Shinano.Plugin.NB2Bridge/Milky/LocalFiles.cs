using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;

namespace Shinano.Plugin.NB2Bridge.Milky;

/// <summary>
/// NB2 插件发送的本地文件（<c>file://</c>）对 LLBot 不可读：上传到插件 bucket 后换成 IM 可下载的预签名链接。
/// 对象保留供宿主归档经同一链接读取，由每小时的清理任务删除 1 小时前的对象。
/// </summary>
public sealed class LocalFiles
{
    /// <summary>插件 bucket 的用途，与 plugin.json 的 bucketPurposes 一致。</summary>
    public const string BUCKET_PURPOSE = "nb2";

    /// <summary>对象保留时间：足够宿主归档读取。</summary>
    public static readonly TimeSpan ObjectRetention = TimeSpan.FromHours(1);

    private const string FILE_SCHEME  = "file://";
    private const string OCTET_STREAM = "application/octet-stream";

    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"]  = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"]  = "image/png",
        [".gif"]  = "image/gif",
        [".webp"] = "image/webp",
        [".mp4"]  = "video/mp4",
        [".mp3"]  = "audio/mpeg",
        [".m4a"]  = "audio/mp4",
        [".flac"] = "audio/flac",
        [".wav"]  = "audio/wav",
        [".amr"]  = "audio/amr"
    };

    private readonly IPluginInitializationContext _context;
    private readonly TimeProvider                 _time;
    private readonly ILogger<LocalFiles>          _logger;
    private          bool                         _bucketReady;

    /// <summary>创建上传器。</summary>
    public LocalFiles(IPluginInitializationContext context, TimeProvider time, ILogger<LocalFiles> logger)
    {
        _context = context;
        _time    = time;
        _logger  = logger;
    }

    /// <summary>把 JSON 中所有 <c>file://</c> 字符串替换为上传后的预签名链接（就地修改）。</summary>
    /// <exception cref="FileNotFoundException">引用的本地文件不存在。</exception>
    public async Task RewriteAsync(JsonNode? node, CancellationToken cancellationToken)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (string key in obj.Select(pair => pair.Key).ToArray())
                {
                    if (FileUri(obj[key]) is { } uri) obj[key] = await UploadAsync(uri, cancellationToken);
                    else await RewriteAsync(obj[key], cancellationToken);
                }

                break;
            case JsonArray array:
                for (int index = 0; index < array.Count; index++)
                {
                    if (FileUri(array[index]) is { } uri) array[index] = await UploadAsync(uri, cancellationToken);
                    else await RewriteAsync(array[index], cancellationToken);
                }

                break;
        }
    }

    /// <summary>删除超过保留时间的对象。</summary>
    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        IPluginBucket bucket = _context.Buckets.Get(BUCKET_PURPOSE);
        DateTimeOffset cutoff = _time.GetUtcNow() - ObjectRetention;
        int deleted = 0;
        await foreach (PluginBucketObject item in bucket.ListAsync(null, cancellationToken))
        {
            if (item.LastModified >= cutoff) continue;
            await bucket.DeleteObjectAsync(item.Key, cancellationToken);
            deleted++;
        }

        _logger.LogInformation("清理 NB2 发送的临时文件：删除 {Count} 个", deleted);
    }

    private static string? FileUri(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) && text.StartsWith(FILE_SCHEME, StringComparison.OrdinalIgnoreCase) ? text : null;

    private async Task<string> UploadAsync(string uri, CancellationToken cancellationToken)
    {
        string path = new Uri(uri).LocalPath;
        IPluginBucket bucket = _context.Buckets.Get(BUCKET_PURPOSE);
        if (!_bucketReady)
        {
            await bucket.EnsureCreatedAsync(cancellationToken);
            _bucketReady = true;
        }

        string extension = Path.GetExtension(path);
        string key = $"{_time.GetUtcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture)}/{Guid.NewGuid():N}{extension}";
        await using FileStream stream = File.OpenRead(path);
        long size = await bucket.PutAsync(key, stream, MediaTypes.GetValueOrDefault(extension, OCTET_STREAM), null, cancellationToken);
        _logger.LogInformation("NB2 发送的本地文件 {Path} 已上传到 {Bucket}/{Key}（{Size} 字节）", path, bucket.Name, key, size);
        return await bucket.CreateImUrlAsync(key);
    }
}
