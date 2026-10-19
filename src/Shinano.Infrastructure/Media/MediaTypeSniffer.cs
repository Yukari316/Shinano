namespace Shinano.Infrastructure.Media;

/// <summary>按文件头识别常见媒体的 MIME 类型；无法识别时返回 <c>application/octet-stream</c>。</summary>
public static class MediaTypeSniffer
{
    /// <summary>无法识别时的类型。</summary>
    public const string OCTET_STREAM = "application/octet-stream";

    private const int HEADER_LENGTH = 16;

    private static readonly Signature[] Signatures =
    [
        Of(0, [0x89, 0x50, 0x4E, 0x47], "image/png"),
        Of(0, [0xFF, 0xD8, 0xFF], "image/jpeg"),
        Of(0, "GIF8"u8.ToArray(), "image/gif"),
        Of(8, "WEBP"u8.ToArray(), "image/webp"),
        Of(0, "BM"u8.ToArray(), "image/bmp"),
        Of(4, "ftyp"u8.ToArray(), "video/mp4"),
        // QQ 语音为 SILK 编码，文件头前可能有一个 0x02 字节。
        Of(0, "#!SILK"u8.ToArray(), "audio/silk"),
        Of(1, "#!SILK"u8.ToArray(), "audio/silk"),
        Of(0, "#!AMR"u8.ToArray(), "audio/amr"),
        Of(0, "ID3"u8.ToArray(), "audio/mpeg"),
        Of(0, "OggS"u8.ToArray(), "audio/ogg")
    ];

    /// <summary>读取文件头识别类型。</summary>
    public static async Task<string> DetectAsync(string filePath, CancellationToken cancellationToken)
    {
        byte[] header = new byte[HEADER_LENGTH];
        int read;
        await using (FileStream stream = File.OpenRead(filePath))
            read = await stream.ReadAtLeastAsync(header, HEADER_LENGTH, throwOnEndOfStream: false, cancellationToken);

        ReadOnlySpan<byte> span = header.AsSpan(0, read);
        foreach (Signature signature in Signatures)
        {
            int end = signature.Offset + signature.Magic.Length;
            if (end <= span.Length && span[signature.Offset..end].SequenceEqual(signature.Magic))
                return signature.MediaType;
        }

        return OCTET_STREAM;
    }

    private static Signature Of(int offset, byte[] magic, string mediaType) =>
        new()
        {
            Offset    = offset,
            Magic     = magic,
            MediaType = mediaType
        };

    private sealed class Signature
    {
        public required int Offset { get; init; }

        public required byte[] Magic { get; init; }

        public required string MediaType { get; init; }
    }
}
