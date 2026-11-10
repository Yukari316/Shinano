namespace Shinano.Host.Plugins.Models;

/// <summary>删除插件包前确认清除数据时显示的范围。</summary>
public sealed class PluginDataPreview
{
    /// <summary>声明的数据表。</summary>
    public required PluginTables Tables { get; init; }

    /// <summary>插件 bucket 的全名。</summary>
    public required string[] Buckets { get; init; }

    /// <summary>是否有机密文件。</summary>
    public required bool SecretsFile { get; init; }

    /// <summary>是否有浏览器用户目录。</summary>
    public required bool BrowserProfile { get; init; }
}
