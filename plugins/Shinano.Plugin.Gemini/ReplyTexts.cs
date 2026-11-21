namespace Shinano.Plugin.Gemini;

/// <summary>Gemini 插件面向聊天会话的固定回复文案；失败原因与技术细节只写日志，不发到聊天。</summary>
internal static class ReplyTexts
{
    public const string NEW_CONVERSATION      = "好，前面聊的我全忘光了！";
    public const string API_KEY_MISSING       = "Gemini 还没通电，现在开不了口喵";
    public const string BUSY                  = "上一个问题还没想完呢，别催！";
    public const string TOO_MANY_IMAGES       = "一口气塞这么多图，眼睛都看花了喵！";
    public const string IMAGE_TOO_LARGE       = "这图也太大了，吞不下去！";
    public const string IMAGE_DOWNLOAD_FAILED = "图被夹了，你找服务器要去";
    public const string EMPTY_RESPONSE        = "Gemini 憋了半天一个字都没憋出来喵";
    public const string REQUEST_TIMEOUT       = "Gemini 想太久睡着了，等会再问吧";
    public const string REQUEST_FAILED        = "Gemini 服务器爆炸了喵";

    public static string Cooldown(int remainingSeconds) => $"问得太快了，歇 {remainingSeconds} 秒再来！";
}
