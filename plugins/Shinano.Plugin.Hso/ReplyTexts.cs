namespace Shinano.Plugin.Hso;

/// <summary>Hso 插件的聊天回复文案（经用户确认，风格见 AGENTS.md「聊天回复文案」）。</summary>
public static class ReplyTexts
{
    /// <summary>冷却中。</summary>
    public const string COOLDOWN = "你冲的太快了，要坏掉了(请等待CD冷却)";

    /// <summary>图库为空。</summary>
    public const string EMPTY = "机器人管理者没有在服务器上塞色图，你去找他要啦！";

    /// <summary>图片（改像素重发后仍）发送失败。</summary>
    public const string SEND_FAILED = "图被夹了喵，你找服务器要去";

    /// <summary>排行标题。</summary>
    public const string RANKING_TITLE = "让我康康到底谁最能冲";

    /// <summary>排行为空。</summary>
    public const string RANKING_EMPTY = "你群连个色批都没有";

    /// <summary><c>AD</c> 导入成功。</summary>
    public const string AD_IMPORTED = "好烧好烧，拿走了喵";

    /// <summary><c>AD</c> 导入的作品页已在图库中。</summary>
    public const string AD_EXISTS = "这张早就有了喵";

    /// <summary><c>AD</c> 导入的作品不存在或页号越界。</summary>
    public const string AD_NOT_FOUND = "没有这张图喵";

    /// <summary><c>AD</c> 导入时图源服务未配置、下载或查询失败。</summary>
    public const string AD_FAILED = "服务器爆炸了喵";
}
