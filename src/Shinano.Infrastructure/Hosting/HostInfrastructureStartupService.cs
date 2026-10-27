using Microsoft.Extensions.Hosting;

namespace Shinano.Infrastructure.Hosting;

/// <summary>在后台执行基础设施启动检查，不阻塞管理面板的启动。</summary>
public sealed class HostInfrastructureStartupService : BackgroundService
{
    private readonly HostInfrastructure _infrastructure;

    /// <summary>创建服务。</summary>
    public HostInfrastructureStartupService(HostInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        _infrastructure.InitializeAsync(stoppingToken);
}
