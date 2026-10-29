namespace Shinano.Tool.Deployment.Models;

/// <summary>部署的运行方式。</summary>
public enum RunMode
{
    /// <summary>直接运行：<c>start</c> 脚本在前台运行 <c>Shinano.Tool run</c>，由它守护宿主与监控。</summary>
    Direct,

    /// <summary>由 Windows 服务管理器或 systemd 托管。</summary>
    Service
}
