namespace Shinano.Plugin.OnlineCheck;

/// <summary>在线检测插件配置。</summary>
public sealed class OnlineCheckConfig
{
    /// <summary>同一群内同一成员戳 Bot 的冷却秒数，默认 20；冷却内再戳会重新计时。</summary>
    public int PokeCooldownSeconds { get; set; } = 20;
}
