namespace Shinano.Host.Restart.Models;

/// <summary>一次带插件包变更的重启的结果。</summary>
public enum RestartOutcome
{
    /// <summary>变更的插件包全部加载成功。</summary>
    Succeeded,

    /// <summary>新版本未能就绪，已还原上一套插件包。</summary>
    RolledBack,

    /// <summary>基础设施处于故障模式，插件包已应用但未经加载验证。</summary>
    Unverified
}
