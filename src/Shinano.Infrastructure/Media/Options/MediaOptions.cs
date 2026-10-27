namespace Shinano.Infrastructure.Media.Options;

/// <summary>媒体存储配置，对应配置节 <c>Shinano:Media</c>。</summary>
public sealed class MediaOptions
{
    /// <summary>配置节路径。</summary>
    public const string SECTION_NAME = "Shinano:Media";

    /// <summary>
    /// 聊天媒体与持久媒体的单文件上限，单位 MiB（1024×1024 字节），默认 100。
    /// </summary>
    public int MaxFileSizeMiB { get; set; } = 100;

    /// <summary>持久媒体最后一条引用删除后到物理删除的宽限期，单位小时，默认 24。</summary>
    public int OrphanGraceHours { get; set; } = 24;
}
