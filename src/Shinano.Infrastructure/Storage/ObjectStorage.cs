using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using System.Net;
using System.Runtime.CompilerServices;

namespace Shinano.Infrastructure.Storage;

/// <summary>
/// RustFS 访问入口。Bot 读写使用内部地址；发给 IM 实现端的预签名链接使用 IM 可达地址签名，
/// 签名包含主机名，因此两个地址各用一个客户端。
/// </summary>
public sealed class ObjectStorage : IDisposable
{
    private const string DEFAULT_REGION    = "us-east-1";
    private const int    DELETE_BATCH_SIZE = 1000;

    private readonly AmazonS3Client _client;
    private readonly AmazonS3Client _imClient;
    private readonly Protocol       _imProtocol;
    private readonly TimeSpan       _presignExpiry;

    /// <summary>创建访问入口，不立即连接。</summary>
    /// <exception cref="ArgumentException">地址或密钥未配置、地址无法解析或有效期不是正数。</exception>
    public ObjectStorage(RustFsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccessKey) || string.IsNullOrWhiteSpace(options.SecretKey))
            throw new ArgumentException("RustFS 密钥未配置");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PresignExpiryMinutes);

        Uri endpoint   = ParseEndpoint(options.Endpoint);
        Uri imEndpoint = string.IsNullOrWhiteSpace(options.ImEndpoint) ? endpoint : ParseEndpoint(options.ImEndpoint);
        BasicAWSCredentials credentials = new(options.AccessKey, options.SecretKey);
        _client        = new AmazonS3Client(credentials, CreateConfig(endpoint));
        _imClient      = new AmazonS3Client(credentials, CreateConfig(imEndpoint));
        _imProtocol    = imEndpoint.Scheme == Uri.UriSchemeHttps ? Protocol.HTTPS : Protocol.HTTP;
        _presignExpiry = TimeSpan.FromMinutes(options.PresignExpiryMinutes);
    }

    /// <summary>检查服务可用性与凭据。</summary>
    /// <exception cref="AmazonServiceException">服务拒绝请求。</exception>
    public async Task CheckAsync(CancellationToken cancellationToken) =>
        await _client.ListBucketsAsync(new ListBucketsRequest(), cancellationToken);

    /// <summary>bucket 不存在时创建。</summary>
    public async Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken)
    {
        if (await AmazonS3Util.DoesS3BucketExistV2Async(_client, bucket)) return;

        try
        {
            await _client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.ErrorCode is "BucketAlreadyOwnedByYou")
        {
            // 并发创建时另一方已完成。
        }
    }

    /// <summary>清空并删除 bucket；bucket 不存在时直接返回。</summary>
    /// <returns>删除的对象个数；bucket 不存在时为 null。</returns>
    public async Task<int?> DeleteBucketAsync(string bucket, CancellationToken cancellationToken)
    {
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(_client, bucket)) return null;

        int deleted = 0;
        ListObjectsV2Request request = new()
        {
            BucketName = bucket,
            MaxKeys    = DELETE_BATCH_SIZE
        };
        ListObjectsV2Response response;
        do
        {
            response = await _client.ListObjectsV2Async(request, cancellationToken);
            List<S3Object> objects = response.S3Objects ?? [];
            if (objects.Count > 0)
            {
                await _client.DeleteObjectsAsync(
                    new DeleteObjectsRequest
                    {
                        BucketName = bucket,
                        Objects    = [.. objects.Select(item => new KeyVersion { Key = item.Key })]
                    },
                    cancellationToken);
                deleted += objects.Count;
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);

        await _client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket }, cancellationToken);
        return deleted;
    }

    /// <summary>上传本地文件。</summary>
    public async Task PutFileAsync(
        string            bucket,
        string            key,
        string            filePath,
        string            contentType,
        CancellationToken cancellationToken) =>
        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName  = bucket,
                Key         = key,
                FilePath    = filePath,
                ContentType = contentType
            },
            cancellationToken);

    /// <summary>读取对象；对象不存在时返回 null。</summary>
    public async Task<StoredObject?> OpenReadAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        try
        {
            GetObjectResponse response = await _client.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = bucket,
                    Key        = key
                },
                cancellationToken);
            return new StoredObject
            {
                Content     = response.ResponseStream,
                ContentType = response.Headers.ContentType ?? "application/octet-stream",
                Length      = response.ContentLength
            };
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>判断对象是否存在。</summary>
    public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        try
        {
            await _client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = bucket,
                    Key        = key
                },
                cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>对象的元数据；对象不存在时返回 null。</summary>
    public async Task<StoredObjectInfo?> GetInfoAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        try
        {
            GetObjectMetadataResponse response = await _client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = bucket,
                    Key        = key
                },
                cancellationToken);
            return new StoredObjectInfo
            {
                Key          = key,
                Size         = response.ContentLength,
                LastModified = ToUtc(response.LastModified)
            };
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>按 key 顺序分页列出对象；<paramref name="prefix" /> 为 null 时列出全部，bucket 不存在时为空。</summary>
    public async IAsyncEnumerable<StoredObjectInfo> ListAsync(
        string                                     bucket,
        string?                                    prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ListObjectsV2Request request = new()
        {
            BucketName = bucket,
            Prefix     = prefix
        };
        while (await ListPageAsync(request, cancellationToken) is { } response)
        {
            foreach (S3Object item in response.S3Objects ?? [])
                yield return new StoredObjectInfo
                {
                    Key          = item.Key,
                    Size         = item.Size ?? 0,
                    LastModified = ToUtc(item.LastModified)
                };

            if (response.IsTruncated != true) yield break;
            request.ContinuationToken = response.NextContinuationToken;
        }
    }

    /// <summary>删除对象；对象不存在时视为成功。</summary>
    public async Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken) =>
        await _client.DeleteObjectAsync(
            new DeleteObjectRequest
            {
                BucketName = bucket,
                Key        = key
            },
            cancellationToken);

    // bucket 不存在时返回 null（迭代器内不能在带 catch 的 try 中 yield）。
    private async Task<ListObjectsV2Response?> ListPageAsync(ListObjectsV2Request request, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ListObjectsV2Async(request, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.ErrorCode == "NoSuchBucket")
        {
            return null;
        }
    }

    // SDK 以本地时间表示修改时间；没有时取 Unix 纪元，调用方据此视为很旧的对象。
    private static DateTimeOffset ToUtc(DateTime? time) =>
        time is { } value ? new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero) : DateTimeOffset.UnixEpoch;

    /// <summary>
    /// 生成 IM 实现端使用的预签名 GET 链接，有效期为配置值。不检查对象是否存在。
    /// </summary>
    public async Task<string> CreateImUrlAsync(string bucket, string key) =>
        await _imClient.GetPreSignedURLAsync(
            new GetPreSignedUrlRequest
            {
                BucketName = bucket,
                Key        = key,
                Verb       = HttpVerb.GET,
                Protocol   = _imProtocol,
                Expires    = DateTime.UtcNow.Add(_presignExpiry)
            });

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
        _imClient.Dispose();
    }

    private static Uri ParseEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"RustFS 地址无效：{value}");
        return uri;
    }

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
