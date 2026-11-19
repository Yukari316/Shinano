using Shinano.Plugin.Hso.Info;

namespace Shinano.Plugin.Hso.Persistence;

/// <summary>
/// 作品信息的补全状态。补全在图片被抽中后或由每日后台任务在后台进行（见 <see cref="HsoCompleter" />），
/// 只有 <see cref="Found" /> 时发送信息卡。数值写入数据库，<c>Shinano.Tool hso-import</c> 写入 <see cref="Pending" />。
/// </summary>
public enum HsoInfoState
{
    /// <summary>尚未查询，或上次查询失败（之后再查）。</summary>
    Pending = 0,

    /// <summary>作品信息已确认：来源原图与图库图片的 SHA256 一致、管理员在审核中确认，或 <c>AD</c> 导入。</summary>
    Found = 1,

    /// <summary>查询完成但没有可信结果，或管理员否决且没有手动补全，不再查询。</summary>
    NotFound = 2,

    /// <summary>
    /// 查到了作品信息，但来源原图与图库图片的 SHA256 不一致或原图下载失败，等待管理员在面板审核；
    /// 记录中已是查到的信息（用于判定 NSFW），审核记录见 <see cref="HsoReviewRecord" />。
    /// </summary>
    Review = 3
}
