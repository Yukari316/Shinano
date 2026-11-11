using Shinano.Infrastructure.Health.Models;

namespace Shinano.Infrastructure.Health;

/// <summary>健康状态的中文名称，健康检查指令与管理面板共用。</summary>
public static class HealthText
{
    /// <summary>健康等级的名称。</summary>
    public static string Describe(HealthLevel level) =>
        level switch
        {
            HealthLevel.Normal   => "正常",
            HealthLevel.Degraded => "降级",
            HealthLevel.Stopped  => "停止",
            _                    => level.ToString()
        };

    /// <summary>组成部分的名称。</summary>
    public static string ComponentName(HealthComponent component) =>
        component switch
        {
            HealthComponent.Startup       => "启动检查",
            HealthComponent.Database      => "数据库",
            HealthComponent.ObjectStorage => "RustFS",
            HealthComponent.Bot           => "LLBot",
            HealthComponent.Plugins       => "插件",
            _                             => component.ToString()
        };
}
