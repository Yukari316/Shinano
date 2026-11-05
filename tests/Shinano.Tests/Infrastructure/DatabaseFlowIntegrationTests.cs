using Microsoft.Extensions.Logging.Abstractions;
using Shinano.Infrastructure.Hosting;
using SqlSugar;

namespace Shinano.Tests.Infrastructure;

/// <summary>
/// <see cref="SqlSugarScope" /> 以 AsyncLocal 为每个异步流程分配客户端，后台任务若继承启动它的流程的上下文，
/// 会与该流程共用同一个连接并发执行命令（Npgsql 报 A command is already in progress）。这里核对调度器的任务使用自己的客户端。
/// </summary>
[Collection(InfrastructureCollection.NAME)]
public sealed class DatabaseFlowIntegrationTests
{
    private const int QUERIES = 40;

    private readonly InfrastructureFixture _fixture;

    public DatabaseFlowIntegrationTests(InfrastructureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SchedulerJob_DoesNotShareTheRegisteringFlowsClient()
    {
        ISqlSugarClient database = _fixture.Ready.Database.Global;
        // 登记前先在当前流程使用数据库，如同插件在 InitializeAsync 中建表后登记任务。
        await database.Ado.GetIntAsync("select 1");

        await using HostScheduler scheduler = new(NullLogger<HostScheduler>.Instance, TimeProvider.System);
        TaskCompletionSource<Exception?> job = new(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Register(
            "test",
            "concurrent-queries",
            TimeSpan.FromHours(1),
            async _ =>
            {
                try
                {
                    for (int index = 0; index < QUERIES; index++) await database.Ado.GetIntAsync("select 1");
                    job.SetResult(null);
                }
                catch (Exception exception)
                {
                    job.SetResult(exception);
                }
            },
            runImmediately: true);

        for (int index = 0; index < QUERIES; index++) await database.Ado.GetIntAsync("select 1");

        Assert.Null(await job.Task.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
    }
}
