using System.ComponentModel.DataAnnotations;

namespace Shinano.Plugin.OnlineCheck.Options;

/// <summary>在线检测插件配置。</summary>
public sealed class OnlineCheckConfig
{
    /// <summary>同一群内同一成员戳 Bot 的冷却秒数，默认 20；冷却内再戳会重新计时。</summary>
    [Display(Name = "戳一戳冷却（秒）", Description = "同一群内同一成员戳 Bot 的冷却时间，冷却内再戳会重新计时")]
    [Range(0, 3600)]
    public int PokeCooldownSeconds { get; set; } = 20;
}
