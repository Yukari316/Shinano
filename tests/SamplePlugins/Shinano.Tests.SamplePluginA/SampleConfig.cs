using System.ComponentModel.DataAnnotations;

namespace Shinano.Tests.SamplePluginA;

/// <summary>测试配置：覆盖标题、说明、顺序、范围、长度与枚举。</summary>
public sealed class SampleConfig
{
    /// <summary>问候语。</summary>
    [Display(Name = "问候语", Description = "config-a 指令回复的内容", Order = 2)]
    [Required]
    [StringLength(20)]
    public string Greeting { get; set; } = "hi";

    /// <summary>次数。</summary>
    [Display(Name = "次数", Order = 1)]
    [Range(1, 10)]
    public int Times { get; set; } = 1;

    /// <summary>模式。</summary>
    public SampleMode Mode { get; set; } = SampleMode.Quiet;
}
