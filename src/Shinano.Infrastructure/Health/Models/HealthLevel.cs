namespace Shinano.Infrastructure.Health.Models;

/// <summary>服务健康等级，数值越大越严重。界面对应 daisyUI 的 success、warning、error。</summary>
public enum HealthLevel
{
    /// <summary>正常（绿）。</summary>
    Normal = 0,

    /// <summary>降级（黄）：部分功能不可用，其余继续服务。</summary>
    Degraded = 1,

    /// <summary>停止（红）：Bot 不响应业务功能。</summary>
    Stopped = 2
}
