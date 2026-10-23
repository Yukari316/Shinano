namespace Shinano.Monitor;

/// <summary>服务健康等级，与主进程的三级一致，对应 daisyUI 语义色 success、warning、error。</summary>
public enum HealthLevel
{
    /// <summary>正常。</summary>
    Normal,

    /// <summary>降级。</summary>
    Degraded,

    /// <summary>停止。</summary>
    Stopped
}
