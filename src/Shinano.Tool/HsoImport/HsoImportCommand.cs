using Amazon.Runtime;
using Npgsql;
using Shinano.Tool.Cli;
using Shinano.Tool.Configuration;
using Shinano.Tool.Deployment;
using Shinano.Tool.HsoImport.Models;
using Shinano.Tool.Setup;

namespace Shinano.Tool.HsoImport;

/// <summary>
/// <c>hso-import --dir &lt;目录&gt;</c>：把本地目录（含子目录）中的图片批量导入 Hso 插件图库。按 SHA256 去重后上传到插件 bucket，
/// 并写入待补全作品信息的记录。数据库与 RustFS 默认取本部署的本地配置，也可用选项指定。
/// </summary>
public static class HsoImportCommand
{
    /// <summary>该命令接受的选项。</summary>
    public static readonly IReadOnlyCollection<string> Options =
        [DIR_OPTION, DSN_OPTION, RUSTFS_ENDPOINT_OPTION, ACCESS_KEY_OPTION, SECRET_KEY_OPTION, BUCKET_PREFIX_OPTION];

    private const string DIR_OPTION             = "dir";
    private const string DSN_OPTION             = "dsn";
    private const string RUSTFS_ENDPOINT_OPTION = "rustfs-endpoint";
    private const string ACCESS_KEY_OPTION      = "access-key";
    private const string SECRET_KEY_OPTION      = "secret-key";
    private const string BUCKET_PREFIX_OPTION   = "bucket-prefix";
    private const char   STORED_SEPARATOR       = '/';

    private const string USAGE =
        "用法：hso-import --dir <图片目录> [--dsn <DSN>] [--rustfs-endpoint <地址>] [--access-key <AccessKey>] [--secret-key <SecretKey>] [--bucket-prefix <前缀>]";

    /// <summary>执行导入。</summary>
    /// <returns>全部文件处理成功（含跳过重复与不支持的文件）时为 0，有文件导入失败时为 1。</returns>
    public static async Task<int> RunAsync(ToolContext context)
    {
        string directory = context.Arguments.Value(DIR_OPTION) is { Length: > 0 } value ? Path.GetFullPath(value) : throw new ToolException(USAGE);
        if (!Directory.Exists(directory)) throw new ToolException($"目录不存在：{directory}");

        string[] files = ListFiles(directory);
        GalleryTarget target = ResolveTarget(context);
        ToolLog log = context.Log;
        await using GalleryStore store = await GalleryStore.OpenAsync(target);
        log.Info(
            $"Hso 图库导入开始：目录 {directory}，{files.Length} 个文件，"
            + $"数据库 {target.Database.Host}:{target.Database.Port}/{target.Database.Database}，bucket {target.Bucket}（{target.Endpoint}）");

        Dictionary<ImportOutcome, int> counts = Enum.GetValues<ImportOutcome>().ToDictionary(outcome => outcome, _ => 0);
        for (int index = 0; index < files.Length; index++)
            counts[await ImportFileAsync(store, target.Bucket, directory, files[index], $"[{index + 1}/{files.Length}]", log)]++;

        string summary = $"Hso 图库导入结束：导入 {counts[ImportOutcome.Imported]}，重复 {counts[ImportOutcome.Duplicate]}，"
                         + $"不支持 {counts[ImportOutcome.Unsupported]}，失败 {counts[ImportOutcome.Failed]}";
        if (counts[ImportOutcome.Failed] == 0)
        {
            log.Info(summary);
            return 0;
        }

        log.Error($"{summary}；处理失败原因后重新运行即可，已导入的图片会按 SHA256 跳过");
        return 1;
    }

    private static async Task<ImportOutcome> ImportFileAsync(GalleryStore store, string bucket, string directory, string path, string progress, ToolLog log)
    {
        string fileName = StoredFileName(directory, path);
        try
        {
            await using FileStream stream = File.OpenRead(path);
            byte[] header = new byte[GalleryFiles.HEADER_LENGTH];
            int read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
            if (GalleryFiles.Detect(header.AsSpan(0, read)) is not { } format)
            {
                log.Say($"{progress} 跳过不支持的文件（不是 JPEG、PNG、GIF 或 WebP）：{fileName}");
                return ImportOutcome.Unsupported;
            }

            stream.Position = 0;
            string sha256 = await GalleryFiles.Sha256Async(stream);
            if (await store.ContainsAsync(sha256))
            {
                log.Say($"{progress} 跳过图库中已有的图片：{fileName}");
                return ImportOutcome.Duplicate;
            }

            PixivPage? pixiv = GalleryFiles.ParsePixivName(fileName);
            GalleryImage image = new()
            {
                ObjectKey = GalleryStore.IMAGE_PREFIX + sha256 + format.Extension,
                Sha256    = sha256,
                MediaType = format.MediaType,
                Size      = stream.Length,
                FileName  = fileName,
                Pid       = pixiv?.Pid,
                Page      = pixiv?.Page
            };
            if (!await store.AddAsync(image, stream))
            {
                log.Say($"{progress} 跳过图库中已有的图片（导入期间由其他来源写入）：{fileName}");
                return ImportOutcome.Duplicate;
            }

            string pixivText = pixiv is null ? "无 pid" : $"pid {pixiv.Pid} 页 {pixiv.Page}";
            log.Info($"{progress} 已导入 {fileName}：{bucket}/{image.ObjectKey}，{format.MediaType}，{image.Size} 字节，{pixivText}");
            return ImportOutcome.Imported;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NpgsqlException or AmazonServiceException
                                              or AmazonClientException or HttpRequestException or TaskCanceledException)
        {
            log.Error($"{progress} 导入失败：{fileName}：{exception.Message}");
            return ImportOutcome.Failed;
        }
    }

    private static string[] ListFiles(string directory)
    {
        try
        {
            return [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ToolException($"无法列出目录 {directory} 中的文件：{exception.Message}");
        }
    }

    // 记录相对导入目录的路径；超过列长度时只记录文件名，Pixiv 命名本来就只看文件名。
    private static string StoredFileName(string directory, string path)
    {
        string relative = Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, STORED_SEPARATOR);
        if (relative.Length <= GalleryStore.FILE_NAME_MAX_LENGTH) return relative;

        string name = Path.GetFileName(path);
        return name.Length <= GalleryStore.FILE_NAME_MAX_LENGTH ? name : name[..GalleryStore.FILE_NAME_MAX_LENGTH];
    }

    private static GalleryTarget ResolveTarget(ToolContext context)
    {
        LocalConfig config = LocalConfig.Load(context.Layout.LocalConfigPath);
        string dsn = Pick(context, config, DSN_OPTION, ConfigKeys.DATABASE_DSN)
                     ?? throw new ToolException($"没有数据库 DSN：请先运行 init，或用 --{DSN_OPTION} 指定");
        string endpointText = Pick(context, config, RUSTFS_ENDPOINT_OPTION, ConfigKeys.RUSTFS_ENDPOINT)
                              ?? throw new ToolException($"没有 RustFS 地址：请先运行 init，或用 --{RUSTFS_ENDPOINT_OPTION} 指定");
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out Uri? endpoint) || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            throw new ToolException($"RustFS 地址无效，需要 http:// 或 https:// 开头的地址：{endpointText}");

        string prefix = Pick(context, config, BUCKET_PREFIX_OPTION, ConfigKeys.BUCKET_PREFIX) ?? ConfigKeys.DEFAULT_BUCKET_PREFIX;
        return new GalleryTarget
        {
            Database  = DsnFormat.Parse(dsn),
            Endpoint  = endpoint,
            AccessKey = Pick(context, config, ACCESS_KEY_OPTION, ConfigKeys.RUSTFS_ACCESS_KEY)
                        ?? throw new ToolException($"没有 RustFS AccessKey：请先运行 init，或用 --{ACCESS_KEY_OPTION} 指定"),
            SecretKey = Pick(context, config, SECRET_KEY_OPTION, ConfigKeys.RUSTFS_SECRET_KEY)
                        ?? throw new ToolException($"没有 RustFS SecretKey：请先运行 init，或用 --{SECRET_KEY_OPTION} 指定"),
            Bucket    = GalleryStore.BucketName(prefix)
        };
    }

    // 选项优先，空值视为未给出；否则取本地配置。
    private static string? Pick(ToolContext context, LocalConfig config, string option, string key) =>
        context.Arguments.Value(option) is { Length: > 0 } value ? value : config.GetString(key);
}
