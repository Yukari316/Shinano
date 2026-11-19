namespace Shinano.Plugin.Hso.Info;

/// <summary>作品信息补全的结果。</summary>
public sealed class HsoLookup
{
    /// <summary>结果类别。</summary>
    public required HsoLookupStatus Status { get; init; }

    /// <summary>查到的作品信息；<see cref="Status" /> 不是 <see cref="HsoLookupStatus.Found" /> 时为 null。</summary>
    public HsoWorkInfo? Info { get; init; }
}

/// <summary>一次作品信息补全的结果类别。</summary>
public enum HsoLookupStatus
{
    /// <summary>查到了作品信息。</summary>
    Found,

    /// <summary>查询完成但没有可信结果。</summary>
    NotFound,

    /// <summary>图源服务未配置、出错或额度用完，下次再查。</summary>
    Failed
}

/// <summary>作品信息的查询方式，数值写入数据库。</summary>
public enum HsoLookupMethod
{
    /// <summary>按记录中的 pid（文件名或 <c>AD</c> 导入）查 Pixiv。</summary>
    Pixiv = 0,

    /// <summary>没有 pid，用 SauceNAO 以图搜图。</summary>
    SauceNao = 1
}
