namespace Shinano.Infrastructure.Health.Models;

/// <summary>参与健康汇总的组成部分。</summary>
public enum HealthComponent
{
    /// <summary>启动检查；失败时宿主进入故障模式（停止）。</summary>
    Startup,

    /// <summary>PostgreSQL；离线时停止。</summary>
    Database,

    /// <summary>RustFS；离线时降级。</summary>
    ObjectStorage,

    /// <summary>LLBot 连接；断线时停止。</summary>
    Bot,

    /// <summary>插件；有插件初始化失败时降级。</summary>
    Plugins
}
