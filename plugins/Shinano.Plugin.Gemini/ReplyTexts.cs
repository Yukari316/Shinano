namespace Shinano.Plugin.Gemini;

/// <summary>Gemini 插件面向聊天会话的固定回复文案。</summary>
internal static class ReplyTexts
{
    public const string NEW_CONVERSATION      = "已开启新对话，当前会话上下文已清空。";
    public const string API_KEY_MISSING       = "Gemini 插件尚未配置 API Key，请联系管理员在管理面板机密设置中填写。";
    public const string BUSY                  = "上一条 Gemini 请求仍在生成中，请稍候完成后再试。";
    public const string TOO_MANY_IMAGES       = "单次对话最多附带 5 张图片，请减少图片数量后重试。";
    public const string IMAGE_TOO_LARGE       = "图片体积超过 20 MB 上限，无法发送给 Gemini。";
    public const string IMAGE_DOWNLOAD_FAILED = "未能下载消息中的图片，请重新发送后重试。";
    public const string EMPTY_RESPONSE        = "Gemini 未返回有效内容，本次不计入对话轮数。";
    public const string REQUEST_TIMEOUT       = "Gemini 请求超时，请稍后重试（本次不计入对话轮数）。";
    public const string REQUEST_FAILED_PREFIX = "Gemini 调用失败：";

    public static string Cooldown(int remainingSeconds) => $"请求过于频繁，请在 {remainingSeconds} 秒后再试。";
}
