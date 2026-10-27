using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shinano.Infrastructure.ChatArchive.Options;
using Shinano.Infrastructure.Configuration;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Database.Options;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Health.Models;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Hosting.Options;
using Shinano.Infrastructure.Media.Options;
using Shinano.Infrastructure.Scheduling;
using Shinano.Infrastructure.Storage;
using Shinano.Infrastructure.Storage.Options;

namespace Shinano.Tests.Infrastructure;

/// <summary>
/// 一次测试运行共用的真实基础设施：独立的 schema 与 bucket 前缀，结束时删除本次创建的全部对象。
/// </summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    /// <summary>测试使用的机器人账号。</summary>
    public const long SELF_ID = 10001;

    private readonly string _runId = $"t{DateTime.UtcNow:MMddHHmmss}{Random.Shared.Next(0, 1000):D3}";

    /// <summary>schema 名称。</summary>
    public SchemaNames Schemas { get; private set; } = new();

    /// <summary>bucket 前缀。</summary>
    public string BucketPrefix { get; private set; } = string.Empty;

    /// <summary>运行健康状态。</summary>
    public RuntimeHealth Health { get; } = new(TimeProvider.System);

    /// <summary>已初始化的基础设施；外部服务未配置时为 null。</summary>
    public HostInfrastructure? Infrastructure { get; private set; }

    /// <summary>就绪的基础设施；未配置时跳过测试。</summary>
    public HostInfrastructure Ready
    {
        get
        {
            TestEnvironment.RequireConfigured();
            // SAFE: 已配置时 InitializeAsync 必然创建了实例。
            return Infrastructure!;
        }
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        if (!TestEnvironment.IsConfigured) return;

        Schemas = new SchemaNames
        {
            Global        = $"{_runId}_global",
            AccountPrefix = $"{_runId}_user_"
        };
        BucketPrefix = $"shinano-debug-test-{_runId}";
        HostScheduler scheduler = new(NullLogger<HostScheduler>.Instance, new FakeTimeProvider());
        Infrastructure = new HostInfrastructure(
            new HostInfrastructureSettings
            {
                Deployment = new DeploymentOptions
                {
                    BucketPrefix = BucketPrefix
                },
                Database = new DatabaseOptions
                {
                    // SAFE: IsConfigured 已确认非空。
                    Dsn = TestEnvironment.Dsn!
                },
                RustFs = new RustFsOptions
                {
                    Endpoint  = TestEnvironment.S3Endpoint!,
                    AccessKey = TestEnvironment.S3AccessKey!,
                    SecretKey = TestEnvironment.S3SecretKey!
                },
                Media = new MediaOptions
                {
                    MaxFileSizeMiB = 1
                },
                ChatArchive = new ChatArchiveOptions(),
                Schemas     = Schemas
            },
            Health,
            scheduler,
            TimeProvider.System,
            NullLoggerFactory.Instance);
        bool ready = await Infrastructure.InitializeAsync(CancellationToken.None);
        Assert.True(ready, Health.Get(HealthComponent.Startup).Detail);
        await Infrastructure.InitializeAccountAsync(SELF_ID, CancellationToken.None);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Infrastructure is null) return;

        if (await Infrastructure.Ready)
        {
            await Infrastructure.Storage.DeleteBucketAsync(BucketNames.Chat(BucketPrefix), CancellationToken.None);
            await Infrastructure.Storage.DeleteBucketAsync(BucketNames.Media(BucketPrefix), CancellationToken.None);
            await using NpgsqlConnection connection = await Infrastructure.Database.OpenConnectionAsync(CancellationToken.None);
            foreach (string schema in new[] { Schemas.Global, Schemas.Account(SELF_ID) })
            {
                await using NpgsqlCommand command = new($"drop schema if exists {schema} cascade", connection);
                await command.ExecuteNonQueryAsync();
            }
        }

        Infrastructure.Dispose();
    }
}
