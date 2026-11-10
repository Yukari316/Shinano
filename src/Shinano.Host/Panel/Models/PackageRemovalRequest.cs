namespace Shinano.Host.Panel.Models;

/// <summary>暂存删除插件包的请求。</summary>
public sealed class PackageRemovalRequest
{
    /// <summary>是否同时清除插件数据；清除在重启确认成功后执行。</summary>
    public bool PurgeData { get; init; }
}
