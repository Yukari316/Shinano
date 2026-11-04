using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Npgsql;
using NpgsqlTypes;
using Shinano.Tool.Cli;
using Shinano.Tool.HsoImport.Models;

namespace Shinano.Tool.HsoImport;

/// <summary>
/// 直接写入 Hso 插件的图库：全局 schema 的 <c>hso_image</c> 表与插件 bucket。工具不引用插件，表名、列名、对象 key 前缀、
/// bucket 用途名与枚举数值都必须与插件一致，由契约测试核对。表与 bucket 由插件初始化时创建，工具不创建。
/// </summary>
public sealed class GalleryStore : IAsyncDisposable
{
    /// <summary>图库表所在的全局 schema，与宿主的默认 schema 名一致。</summary>
    public const string SCHEMA = "global";

    /// <summary>图库表名。</summary>
    public const string TABLE = "hso_image";

    /// <summary>插件 bucket 的用途名，bucket 名为 <c>&lt;前缀&gt;-hso</c>。</summary>
    public const string BUCKET_PURPOSE = "hso";

    /// <summary>对象 key 前缀，完整 key 为前缀加内容 SHA256 加扩展名。</summary>
    public const string IMAGE_PREFIX = "images/";

    /// <summary>导入方式列的取值：插件 <c>HsoImageOrigin.Tool</c> 的数值。</summary>
    public const int ORIGIN_TOOL = 3;

    /// <summary>补全状态列的取值：插件 <c>HsoInfoState.Pending</c> 的数值，作品信息留待图片被抽中时补全。</summary>
    public const int INFO_STATE_PENDING = 0;

    /// <summary>文件名列的最大长度（字符）。</summary>
    public const int FILE_NAME_MAX_LENGTH = 255;

    /// <summary>内容 SHA256 列，表上有唯一索引。</summary>
    public const string SHA256_COLUMN = "sha256";

    private const string OBJECT_KEY_COLUMN  = "object_key";
    private const string MEDIA_TYPE_COLUMN  = "media_type";
    private const string SIZE_COLUMN        = "size";
    private const string FILE_NAME_COLUMN   = "file_name";
    private const string PID_COLUMN         = "pid";
    private const string PAGE_COLUMN        = "page";
    private const string NSFW_COLUMN        = "nsfw";
    private const string NSFW_MANUAL_COLUMN = "nsfw_manual";
    private const string BLOCKED_COLUMN     = "blocked";
    private const string INFO_STATE_COLUMN  = "info_state";
    private const string ORIGIN_COLUMN      = "origin";
    private const string IMPORTED_AT_COLUMN = "imported_at";
    private const string DEFAULT_REGION     = "us-east-1";
    private const string NO_SUCH_BUCKET     = "NoSuchBucket";

    /// <summary>插入时写入的列；其余列（ID 与作品信息）取表默认值或 null。</summary>
    public static readonly IReadOnlyList<string> InsertedColumns =
    [
        OBJECT_KEY_COLUMN, SHA256_COLUMN, MEDIA_TYPE_COLUMN, SIZE_COLUMN, FILE_NAME_COLUMN, PID_COLUMN, PAGE_COLUMN, NSFW_COLUMN,
        NSFW_MANUAL_COLUMN, BLOCKED_COLUMN, INFO_STATE_COLUMN, ORIGIN_COLUMN, IMPORTED_AT_COLUMN
    ];

    // SQL_NEED: 工具不引用 SqlSugar，表名与列名是上面经契约测试核对的常量；on conflict 防止与面板上传或另一次导入并发时重复插入。
    private static readonly string InsertSql =
        $"insert into {SCHEMA}.{TABLE} ({string.Join(", ", InsertedColumns)}) "
        + $"values ({string.Join(", ", InsertedColumns.Select(column => "@" + column))}) "
        + $"on conflict ({SHA256_COLUMN}) do nothing";

    // SQL_NEED: 工具不引用 SqlSugar，按常量表名查询。
    private static readonly string ContainsSql = $"select exists(select 1 from {SCHEMA}.{TABLE} where {SHA256_COLUMN} = @{SHA256_COLUMN})";

    private readonly NpgsqlConnection _connection;
    private readonly AmazonS3Client   _client;
    private readonly string           _bucket;

    private GalleryStore(NpgsqlConnection connection, AmazonS3Client client, string bucket)
    {
        _connection = connection;
        _client     = client;
        _bucket     = bucket;
    }

    /// <summary>部署的 Hso 插件 bucket 名。</summary>
    public static string BucketName(string prefix) => $"{prefix}-{BUCKET_PURPOSE}";

    /// <summary>连接数据库与 RustFS，并确认插件已创建图库表与 bucket。</summary>
    /// <exception cref="ToolException">无法连接，或 Hso 插件尚未初始化。</exception>
    public static async Task<GalleryStore> OpenAsync(GalleryTarget target)
    {
        NpgsqlConnection connection = new(target.Database.ConnectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            await connection.DisposeAsync();
            // 只报告目标与服务端消息，不回显连接串。
            throw new ToolException(
                $"无法以 {target.Database.Username} 连接 {target.Database.Host}:{target.Database.Port}/{target.Database.Database}：{exception.Message}");
        }

        AmazonS3Client client = new(new BasicAWSCredentials(target.AccessKey, target.SecretKey), CreateConfig(target.Endpoint));
        GalleryStore store = new(connection, client, target.Bucket);
        try
        {
            await store.RequireInitializedAsync(target.Endpoint);
            return store;
        }
        catch
        {
            await store.DisposeAsync();
            throw;
        }
    }

    /// <summary>图库中是否已有该内容的图片。</summary>
    public async Task<bool> ContainsAsync(string sha256)
    {
        await using NpgsqlCommand command = new(ContainsSql, _connection);
        command.Parameters.AddWithValue(SHA256_COLUMN, sha256);
        return await command.ExecuteScalarAsync() is true;
    }

    /// <summary>
    /// 上传对象并写入图库表。对象 key 由内容决定，同一内容重复上传只覆盖为相同内容；写表失败时不删除对象，
    /// 以免误删并发导入的同一内容，重新运行会覆盖它并补写记录。
    /// </summary>
    /// <param name="image">要写入的图片。</param>
    /// <param name="content">图片内容，从头读取；调用方负责关闭。</param>
    /// <returns>是否插入了新记录；并发导入了同一内容时为 false。</returns>
    public async Task<bool> AddAsync(GalleryImage image, Stream content)
    {
        content.Position = 0;
        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName      = _bucket,
                Key             = image.ObjectKey,
                InputStream     = content,
                AutoCloseStream = false,
                ContentType     = image.MediaType
            });

        await using NpgsqlCommand command = new(InsertSql, _connection);
        command.Parameters.AddWithValue(OBJECT_KEY_COLUMN, image.ObjectKey);
        command.Parameters.AddWithValue(SHA256_COLUMN, image.Sha256);
        command.Parameters.AddWithValue(MEDIA_TYPE_COLUMN, image.MediaType);
        command.Parameters.AddWithValue(SIZE_COLUMN, image.Size);
        command.Parameters.AddWithValue(FILE_NAME_COLUMN, image.FileName);
        command.Parameters.Add(Nullable(PID_COLUMN, NpgsqlDbType.Bigint, image.Pid));
        command.Parameters.Add(Nullable(PAGE_COLUMN, NpgsqlDbType.Integer, image.Page));
        command.Parameters.AddWithValue(NSFW_COLUMN, false);
        command.Parameters.AddWithValue(NSFW_MANUAL_COLUMN, false);
        command.Parameters.AddWithValue(BLOCKED_COLUMN, false);
        command.Parameters.AddWithValue(INFO_STATE_COLUMN, INFO_STATE_PENDING);
        command.Parameters.AddWithValue(ORIGIN_COLUMN, ORIGIN_TOOL);
        command.Parameters.AddWithValue(IMPORTED_AT_COLUMN, DateTimeOffset.UtcNow);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _connection.DisposeAsync();
    }

    private async Task RequireInitializedAsync(Uri endpoint)
    {
        const string NOT_INITIALIZED = "Hso 插件尚未初始化：请先在面板启用一次 Hso 插件，由插件创建图库表与 bucket 后再导入";

        bool tableExists;
        try
        {
            // SQL_NEED: 按限定名检查表是否存在，to_regclass 在表不存在时返回 null 而不报错。
            await using NpgsqlCommand command = new("select to_regclass(@name) is not null", _connection);
            command.Parameters.AddWithValue("name", $"{SCHEMA}.{TABLE}");
            tableExists = await command.ExecuteScalarAsync() is true;
        }
        catch (NpgsqlException exception)
        {
            throw new ToolException($"无法检查图库表 {SCHEMA}.{TABLE}：{exception.Message}");
        }

        if (!tableExists) throw new ToolException($"{NOT_INITIALIZED}（数据库中没有表 {SCHEMA}.{TABLE}）");

        try
        {
            // 列出对象同时核对凭据与 bucket 是否存在。
            await _client.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = _bucket,
                    MaxKeys    = 1
                });
        }
        catch (AmazonS3Exception exception) when (exception.ErrorCode == NO_SUCH_BUCKET)
        {
            throw new ToolException($"{NOT_INITIALIZED}（RustFS 中没有 bucket {_bucket}）");
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or TaskCanceledException)
        {
            throw new ToolException($"无法访问 RustFS {endpoint} 的 bucket {_bucket}：{exception.Message}");
        }
    }

    private static NpgsqlParameter Nullable<T>(string name, NpgsqlDbType type, T? value) where T : struct =>
        new(name, type)
        {
            Value = (object?)value ?? DBNull.Value
        };

    // 与宿主的 RustFS 客户端配置一致：路径风格访问、固定签名区域。
    private static AmazonS3Config CreateConfig(Uri endpoint) =>
        new()
        {
            ServiceURL           = endpoint.ToString(),
            ForcePathStyle       = true,
            AuthenticationRegion = DEFAULT_REGION,
            // 新版 SDK 默认附加 CRC 校验头，兼容 S3 的实现不一定支持，只在操作要求时计算。
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };
}
