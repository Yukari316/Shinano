namespace Shinano.Host.Plugins;

/// <summary><see cref="PluginRuntime" /> 使用的部署设置。</summary>
public sealed class PluginRuntimeSettings
{
    /// <summary>部署数据目录。</summary>
    public required string DataDirectory { get; init; }

    /// <summary>插件目录，相对路径基于部署数据目录。</summary>
    public required string PluginDirectory { get; init; }

    /// <summary>bucket 前缀。</summary>
    public required string BucketPrefix { get; init; }
}
