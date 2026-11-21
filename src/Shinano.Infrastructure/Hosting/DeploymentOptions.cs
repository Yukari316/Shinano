namespace Shinano.Infrastructure.Hosting;

/// <summary>
/// 部署级配置，对应配置节 <c>Shinano:Deployment</c>，由初始化脚本写入部署配置。
/// </summary>
public sealed class DeploymentOptions
{
    /// <summary>配置节路径。</summary>
    public const string SECTION_NAME = "Shinano:Deployment";

    /// <summary>
    /// 计算自然日（聊天分区、统计日期）使用的 IANA 时区，默认 <c>Asia/Shanghai</c>。
    /// 修改只影响此后新建的分区。
    /// </summary>
    public string TimeZone { get; set; } = "Asia/Shanghai";

    /// <summary>本部署的 bucket 前缀，默认 <c>shinano</c>；每个部署必须唯一。</summary>
    public string BucketPrefix { get; set; } = "shinano";

    /// <summary>Bot Super User 的 IM 用户 ID 列表，切换机器人账号后继续生效。</summary>
    public long[] SuperUsers { get; set; } = [];
}
