namespace Shinano.Infrastructure.Media;

/// <summary>媒体超过大小上限，读取或下载已中止。</summary>
public sealed class MediaTooLargeException : Exception
{
    /// <summary>创建异常。</summary>
    /// <param name="limitBytes">上限，单位字节。</param>
    public MediaTooLargeException(long limitBytes)
        : base($"媒体超过大小上限 {limitBytes} 字节")
    {
        LimitBytes = limitBytes;
    }

    /// <summary>上限，单位字节。</summary>
    public long LimitBytes { get; }
}
