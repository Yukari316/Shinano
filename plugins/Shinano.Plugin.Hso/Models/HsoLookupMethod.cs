namespace Shinano.Plugin.Hso.Models;

/// <summary>作品信息的查询方式，数值写入数据库。</summary>
public enum HsoLookupMethod
{
    /// <summary>按记录中的 pid（文件名或 <c>AD</c> 导入）查 Pixiv。</summary>
    Pixiv = 0,

    /// <summary>没有 pid，用 SauceNAO 以图搜图。</summary>
    SauceNao = 1
}
