using System.ComponentModel.DataAnnotations;

namespace Shinano.Plugin.Gemini;

/// <summary>Gemini 插件普通配置，存于数据库并在管理面板通用表单中编辑。</summary>
public sealed class GeminiConfig
{
    /// <summary>触发对话的前缀。</summary>
    [Display(Name = "触发前缀", Description = "消息以此前缀开头时触发 Gemini 对话，如 # 或 /ask", Order = 1)]
    [Required(ErrorMessage = "触发前缀不能为空")]
    [StringLength(16, MinimumLength = 1, ErrorMessage = "触发前缀长度必须在 1 到 16 之间")]
    public string TriggerPrefix { get; set; } = "#";

    /// <summary>群聊单个发言者连续对话的最大轮数，超出后的下一次提问开启新对话。</summary>
    [Display(Name = "群聊最大对话轮数", Description = "群聊中按「群 + 发言者」独立计数；达到上限后的下一次提问清空上下文开启新对话", Order = 2)]
    [Range(1, 100, ErrorMessage = "群聊最大对话轮数必须在 1 到 100 之间")]
    public int GroupMaxTurns { get; set; } = 2;

    /// <summary>好友私聊单个发言者连续对话的最大轮数，超出后的下一次提问开启新对话。</summary>
    [Display(Name = "私聊最大对话轮数", Description = "好友私聊中按发言者独立计数；达到上限后的下一次提问清空上下文开启新对话", Order = 3)]
    [Range(1, 100, ErrorMessage = "私聊最大对话轮数必须在 1 到 100 之间")]
    public int PrivateMaxTurns { get; set; } = 8;

    /// <summary>单个发言者跨群与私聊的全局触发冷却秒数；为 0 时关闭限制。</summary>
    [Display(Name = "发言者冷却（秒）", Description = "同一发言者跨群聊与私聊共享冷却；0 表示不限制", Order = 4)]
    [Range(0, 3600, ErrorMessage = "冷却秒数必须在 0 到 3600 之间")]
    public int UserCooldownSeconds { get; set; } = 30;

    /// <summary>Gemini REST API 根地址，可配置为官方地址或反代/中转地址。</summary>
    [Display(Name = "API Base URL", Description = "Gemini REST API 根地址（不含 /v1beta），默认官方地址", Order = 5)]
    [Required(ErrorMessage = "API Base URL 不能为空")]
    [StringLength(512, MinimumLength = 1, ErrorMessage = "API Base URL 长度无效")]
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";

    /// <summary>可选 HTTP/HTTPS/SOCKS 代理地址。</summary>
    [Display(Name = "HTTP 代理地址（可选）", Description = "留空则直连；例如 http://127.0.0.1:7890", Order = 6)]
    [StringLength(512, ErrorMessage = "代理地址过长")]
    public string ProxyUrl { get; set; } = string.Empty;

    /// <summary>是否在回复卡片顶部展示模型的思考摘要。</summary>
    [Display(Name = "显示思考摘要", Description = "开启后请求模型返回思考摘要，并显示在长文卡片正文顶部（默认关闭）", Order = 7)]
    public bool IncludeThoughts { get; set; }

    /// <summary>是否启用 Google Search 联网搜索工具。</summary>
    [Display(Name = "启用 Google Search 联网搜索", Description = "开启后允许模型调用 Google Search Grounding 检索最新信息（默认关闭）", Order = 8)]
    public bool EnableGoogleSearch { get; set; }

    /// <summary>联网搜索在回复末尾最多列出的参考来源条数。</summary>
    [Display(Name = "联网搜索引用条数上限", Description = "启用联网搜索且返回来源时，在卡片末尾最多显示的去重参考来源数", Order = 9)]
    [Range(1, 20, ErrorMessage = "引用条数上限必须在 1 到 20 之间")]
    public int MaxSearchSources { get; set; } = 5;

    /// <summary>单次 Gemini API 请求超时秒数。</summary>
    [Display(Name = "请求超时（秒）", Description = "等待 Gemini 生成回复的最长秒数", Order = 10)]
    [Range(10, 600, ErrorMessage = "请求超时必须在 10 到 600 秒之间")]
    public int TimeoutSeconds { get; set; } = 120;
}
