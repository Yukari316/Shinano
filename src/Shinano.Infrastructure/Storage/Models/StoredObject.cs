namespace Shinano.Infrastructure.Storage.Models;

/// <summary>读取到的对象内容。调用方负责释放，释放时关闭底层网络流。</summary>
public sealed class StoredObject : IAsyncDisposable
{
    /// <summary>对象内容流，只能顺序读取一次。</summary>
    public required Stream Content { get; init; }

    /// <summary>MIME 类型。</summary>
    public required string ContentType { get; init; }

    /// <summary>字节数。</summary>
    public required long Length { get; init; }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
