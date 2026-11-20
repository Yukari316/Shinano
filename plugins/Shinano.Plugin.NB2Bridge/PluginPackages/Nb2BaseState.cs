namespace Shinano.Plugin.NB2Bridge.PluginPackages;

/// <summary>NB2 运行环境与当前 NB2Bridge 期望的基础锁定文件的关系。</summary>
public enum Nb2BaseState
{
    /// <summary>没有 venv，需要先运行 <c>nb2-setup</c>。</summary>
    Missing,

    /// <summary>基础层与期望一致，插件层已锁定。</summary>
    Current,

    /// <summary>基础层与期望一致，但插件层尚未安装（<c>nb2-setup</c> 刚建立）；启动 NB2 前自动重建。</summary>
    Incomplete,

    /// <summary>实际基础锁定文件与期望不同（NB2Bridge 升级带来了新版），建议重建。</summary>
    Outdated,

    /// <summary>没有实际基础锁定文件：环境不是按锁定文件建立的（旧版建立），建议重建。</summary>
    Unmanaged
}
