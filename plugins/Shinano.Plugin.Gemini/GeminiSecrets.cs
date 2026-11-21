using System.ComponentModel.DataAnnotations;

namespace Shinano.Plugin.Gemini;

/// <summary>Gemini 插件机密，存于部署本地机密文件，管理面板只写不回显。</summary>
public sealed class GeminiSecrets
{
    /// <summary>Gemini API Key。</summary>
    [Display(Name = "Gemini API Key", Description = "Google AI Studio 或兼容中转的 API Key；留空视为未配置", Order = 1)]
    public string ApiKey { get; set; } = string.Empty;
}
