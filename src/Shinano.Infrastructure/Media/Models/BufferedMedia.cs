using System.Security.Cryptography;

namespace Shinano.Infrastructure.Media.Models;

/// <summary>
/// 暂存在本地临时文件中的媒体内容及其 SHA-256。释放时删除临时文件。
/// </summary>
public sealed class BufferedMedia : IAsyncDisposable
{
    private const int COPY_BUFFER_SIZE = 81920;

    private BufferedMedia(string filePath, string sha256, long length)
    {
        FilePath = filePath;
        Sha256   = sha256;
        Length   = length;
    }

    /// <summary>临时文件路径。</summary>
    public string FilePath { get; }

    /// <summary>内容 SHA-256 的小写十六进制。</summary>
    public string Sha256 { get; }

    /// <summary>字节数。</summary>
    public long Length { get; }

    /// <summary>
    /// 把 <paramref name="source" /> 读入临时文件，边读边计数，超过 <paramref name="limitBytes" /> 立即中止。
    /// </summary>
    /// <exception cref="MediaTooLargeException">内容超过上限；临时文件已删除。</exception>
    public static async Task<BufferedMedia> CreateAsync(
        Stream            source,
        long              limitBytes,
        CancellationToken cancellationToken)
    {
        string filePath = Path.GetTempFileName();
        try
        {
            using IncrementalHash hash   = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[]                buffer = new byte[COPY_BUFFER_SIZE];
            long                  length = 0;
            await using (FileStream target = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    length += read;
                    if (length > limitBytes) throw new MediaTooLargeException(limitBytes);
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            return new BufferedMedia(filePath, Convert.ToHexStringLower(hash.GetHashAndReset()), length);
        }
        catch
        {
            File.Delete(filePath);
            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        File.Delete(FilePath);
        return ValueTask.CompletedTask;
    }
}
