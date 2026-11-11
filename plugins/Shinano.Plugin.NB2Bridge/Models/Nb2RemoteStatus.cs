namespace Shinano.Plugin.NB2Bridge.Models;

/// <summary>NB2 控制插件 <c>/shinano/status</c> 返回的运行环境版本（只取用到的字段）。</summary>
public sealed class Nb2RemoteStatus
{
    /// <summary>nonebot2 包版本，如 <c>2.5.0</c>。</summary>
    public required string Nonebot { get; init; }

    /// <summary>Python 版本，如 <c>3.12.11</c>。</summary>
    public required string Python { get; init; }
}
