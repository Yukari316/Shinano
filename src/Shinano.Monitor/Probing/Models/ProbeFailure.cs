namespace Shinano.Monitor.Probing.Models;

/// <summary>探测失败的类别。状态页只显示类别，不显示原始异常、地址与凭据。</summary>
public enum ProbeFailure
{
    /// <summary>部署配置中缺少该服务的地址或凭据。</summary>
    NotConfigured,

    /// <summary>超过探测超时。</summary>
    Timeout,

    /// <summary>连接被拒绝或无法建立。</summary>
    ConnectionFailed,

    /// <summary>认证失败。</summary>
    AuthenticationFailed,

    /// <summary>返回了非成功的 HTTP 状态码。</summary>
    HttpStatus,

    /// <summary>主进程处于故障模式。</summary>
    FaultMode,

    /// <summary>主进程仍在启动。</summary>
    Starting,

    /// <summary>主进程报告了降级或停止的健康项。</summary>
    Unhealthy,

    /// <summary>其他错误。</summary>
    Error
}
