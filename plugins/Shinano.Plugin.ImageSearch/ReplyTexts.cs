namespace Shinano.Plugin.ImageSearch;

/// <summary>搜图插件的聊天回复文案（经用户确认，风格见 AGENTS.md「聊天回复文案」）。</summary>
public static class ReplyTexts
{
    /// <summary>等待发图的提示，<c>{0}</c> 为等待时长，如「1分钟」。</summary>
    public const string WAITING = "图呢(请在{0}内发送图片)";

    /// <summary>「搜图」收到图片、开始搜索时的确认：用户发出的图片可能被腾讯拦截而 Bot 收不到，回复让发起人知道图已收到。</summary>
    public const string RECEIVED_IMAGE = "图收到了，正在冲喵";

    /// <summary>「搜番」收到图片、开始搜索时的确认。</summary>
    public const string RECEIVED_ANIME = "图收到了，正在找番喵";

    /// <summary>等待超时。</summary>
    public const string NO_IMAGE = "连图都没有真是太逊了";

    /// <summary>指令冷却中。</summary>
    public const string COOLDOWN = "你冲的太快了，要坏掉了(请等待CD冷却)";

    /// <summary>插件没有图源服务令牌、令牌被拒，或网关没有搜索服务的凭据。</summary>
    public const string UNCONFIGURED = "冲不出来喵";

    /// <summary>搜索服务的额度用完，且兜底也没有结果。</summary>
    public const string RATE_LIMITED = "已经冲尽了";

    /// <summary>网关、网络或搜索服务出错。</summary>
    public const string SERVER_ERROR = "服务器爆炸了喵";

    /// <summary>没有结果。</summary>
    public const string NOT_FOUND = "没有这种色图喵";

    /// <summary>原图发送失败（下载、转存或发送任一步）。</summary>
    public const string SEND_FAILED = "图被夹了喵，你找服务器要去";

    /// <summary>Pixiv 作品已被删除且没有其他来源。</summary>
    public const string DELETED = "哈哈，图被删了喵";
}
