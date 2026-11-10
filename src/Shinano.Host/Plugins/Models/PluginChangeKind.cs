namespace Shinano.Host.Plugins.Models;

/// <summary>插件包变更的类型。</summary>
public enum PluginChangeKind
{
    /// <summary>安装新插件。</summary>
    Install,

    /// <summary>以新的插件包替换已安装的同 ID 插件，允许降级。</summary>
    Upgrade,

    /// <summary>删除插件包；插件数据保留，除非变更选择了 <see cref="PluginChange.PurgeData" />。</summary>
    Remove
}
