using System.Globalization;

namespace Shinano.Plugin.Bilibili;

/// <summary>B站订阅插件的聊天文案（经用户确认，风格见 AGENTS.md「聊天回复文案」）。</summary>
public static class ReplyTexts
{
    /// <summary>私聊发送二维码时附带的说明。</summary>
    public const string QR_PROMPT = "用B站扫一下，三分钟内有效喵";

    /// <summary>扫码登录成功。</summary>
    public const string LOGIN_SUCCEEDED = "登上了！是「{0}」喵";

    /// <summary>二维码过期。</summary>
    public const string QR_EXPIRED = "二维码过期了，你手太慢了喵，再发一次「B站登录」吧";

    /// <summary>接口或浏览器出错导致登录失败。</summary>
    public const string LOGIN_FAILED = "B站服务器爆炸了喵，再发一次「B站登录」试试";

    /// <summary>已有登录时确认是否退出。</summary>
    public const string CONFIRM_LOGOUT = "现在登着「{0}」（UID {1}），要退出换号吗？(y/n)";

    /// <summary>回复 n，不退出原账号。</summary>
    public const string LOGOUT_DECLINED = "好，不换了喵";

    /// <summary>等待确认超时。</summary>
    public const string LOGOUT_TIMEOUT = "半天不说话，不换了喵";

    /// <summary>动态推送合并转发的第一条。</summary>
    public const string DYNAMIC_HEADER = "{0}更新了喵";

    /// <summary>动态推送合并转发的第二条。</summary>
    public const string DYNAMIC_COUNT = "一共{0}条动态喵";

    /// <summary>卡片正文被截断时附加内容的第一条，填入动态 ID。</summary>
    public const string TEXT_TOO_LONG = "原文太长了喵！要坏掉了喵！\nhttps://t.bilibili.com/{0}";

    /// <summary>开播。</summary>
    public const string LIVE_STARTED = "{0} 直播了喵\nhttps://live.bilibili.com/{1}";

    /// <summary>下播。</summary>
    public const string LIVE_ENDED = "{0} 下播了喵！\n播了{1}喵";

    private const string UNDER_A_MINUTE = "不到1分钟";

    /// <summary>填入文案的占位值。</summary>
    public static string Format(string template, params object[] values) => string.Format(CultureInfo.InvariantCulture, template, values);

    /// <summary>直播时长：「X小时Y分钟」，不足 1 小时省略小时，不足 1 分钟为「不到1分钟」。</summary>
    public static string Duration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1)) return UNDER_A_MINUTE;
        int hours = (int)duration.TotalHours;
        return hours > 0 ? $"{hours}小时{duration.Minutes}分钟" : $"{duration.Minutes}分钟";
    }
}
