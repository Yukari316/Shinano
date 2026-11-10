namespace Shinano.Host.Plugins.Models;

/// <summary>插件数据清除的步骤，按声明顺序执行。</summary>
public enum PluginPurgeStepKind
{
    /// <summary>删除声明的数据表。</summary>
    Tables,

    /// <summary>删除各 schema 中该插件的结构版本记录。</summary>
    SchemaVersions,

    /// <summary>删除持久媒体引用。</summary>
    MediaReferences,

    /// <summary>清空并删除插件 bucket。</summary>
    Buckets,

    /// <summary>删除配置、启用状态与顺序。</summary>
    ConfigAndState,

    /// <summary>删除机密文件。</summary>
    Secrets,

    /// <summary>删除浏览器用户目录。</summary>
    BrowserProfile,

    /// <summary>查找名字以插件前缀开头、但未声明的表，只报告不删除。</summary>
    Leftovers
}
