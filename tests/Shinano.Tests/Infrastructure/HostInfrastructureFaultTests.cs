using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
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
using Shinano.Infrastructure.Storage.Options;

namespace Shinano.Tests.Infrastructure;

public sealed class HostInfrastructureFaultTests
{
    [Theory]
    [InlineData("")]
    [InlineData("postgres://user:pass@127.0.0.1:1/db")]
    public async Task UnusableDatabase_EntersFaultMode(string dsn)
    {
        RuntimeHealth health = new(TimeProvider.System);
        using HostInfrastructure infrastructure = new(
            new HostInfrastructureSettings
            {
                Deployment  = new DeploymentOptions(),
                Database    = new DatabaseOptions { Dsn = dsn },
                RustFs      = new RustFsOptions(),
                Media       = new MediaOptions(),
                ChatArchive = new ChatArchiveOptions()
            },
            health,
            new HostScheduler(NullLogger<HostScheduler>.Instance, new FakeTimeProvider()),
            TimeProvider.System,
            NullLoggerFactory.Instance);

        bool ready = await infrastructure.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(ready);
        Assert.False(await infrastructure.Ready);
        Assert.Equal(HealthLevel.Stopped, health.Overall);
        Assert.NotEmpty(health.Get(HealthComponent.Startup).Detail);
        Assert.Throws<InvalidOperationException>(() => infrastructure.Database);
    }

    [Fact]
    public void Dsn_UriForm_IsConvertedWithUtcSession()
    {
        Npgsql.NpgsqlConnectionStringBuilder builder =
            PostgresConnectionString.Parse("postgres://shinano:p%40ss@10.0.0.1:11451/shinano_debug");

        Assert.Equal("10.0.0.1", builder.Host);
        Assert.Equal(11451, builder.Port);
        Assert.Equal("shinano", builder.Username);
        Assert.Equal("p@ss", builder.Password);
        Assert.Equal("shinano_debug", builder.Database);
        Assert.Equal("UTC", builder.Timezone);
    }
}
