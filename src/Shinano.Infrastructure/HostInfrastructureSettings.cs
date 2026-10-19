using Shinano.Infrastructure.ChatArchive;
using Shinano.Infrastructure.Configuration;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Media;
using Shinano.Infrastructure.Storage;

namespace Shinano.Infrastructure;

/// <summary><see cref="HostInfrastructure" /> 使用的全部配置。</summary>
public sealed class HostInfrastructureSettings
{
    /// <summary>部署配置。</summary>
    public required DeploymentOptions Deployment { get; init; }

    /// <summary>数据库配置。</summary>
    public required DatabaseOptions Database { get; init; }

    /// <summary>RustFS 配置。</summary>
    public required RustFsOptions RustFs { get; init; }

    /// <summary>媒体配置。</summary>
    public required MediaOptions Media { get; init; }

    /// <summary>聊天记录配置。</summary>
    public required ChatArchiveOptions ChatArchive { get; init; }

    /// <summary>schema 名称，生产环境使用默认值。</summary>
    public SchemaNames Schemas { get; init; } = new();
}
