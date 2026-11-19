namespace Shinano.Host.Plugins;

/// <summary>插件配置，对应配置节 <c>Shinano:Plugins</c>。</summary>
public sealed class PluginOptions
{
    /// <summary>配置节路径。</summary>
    public const string SECTION_NAME = "Shinano:Plugins";

    /// <summary>
    /// 已安装插件的目录，每个插件一个子目录；相对路径基于部署数据目录，默认 <c>plugins</c>。
    /// </summary>
    public string Directory { get; set; } = "plugins";
}
