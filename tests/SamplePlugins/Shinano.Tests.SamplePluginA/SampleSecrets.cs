using System.ComponentModel.DataAnnotations;

namespace Shinano.Tests.SamplePluginA;

/// <summary>测试机密：一个可在面板写入的字符串，一个只显示状态的非字符串项。</summary>
public sealed class SampleSecrets
{
    /// <summary>密钥。</summary>
    [Display(Name = "密钥", Description = "secret-a 指令报告是否已配置")]
    public string? ApiKey { get; set; }

    /// <summary>插件自行写入的登录状态，面板只显示是否已配置。</summary>
    public Dictionary<string, string>? Session { get; set; }
}
