namespace Shinano.Tool.Plugins.Models;

/// <summary>暂存变更的类型，名称与宿主一致。</summary>
public enum StagedChangeKind
{
    /// <summary>安装新插件。</summary>
    Install,

    /// <summary>替换已安装的插件。</summary>
    Upgrade,

    /// <summary>删除已安装的插件。</summary>
    Remove
}
