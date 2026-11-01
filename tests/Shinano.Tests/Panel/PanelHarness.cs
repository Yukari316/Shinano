using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shinano.Host.Bot;
using Shinano.Host.Configuration;
using Shinano.Host.Panel;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Models;
using Shinano.Host.Restart;
using Shinano.Host.Restart.Models;
using Shinano.Host.Web;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Hosting.Models;
using Shinano.Infrastructure.Logging;
using Shinano.Infrastructure.Logging.Models;
using Shinano.Tests.Plugins;
using Shinano.Tests.Rendering;
using System.Net;

namespace Shinano.Tests.Panel;

/// <summary>
/// 真实的面板 Web 应用（认证、面板 API、插件 API 端点来源）与 <see cref="PluginHarness" /> 的插件运行时，
/// 监听回环地址的随机端口，部署数据目录为临时目录。
/// </summary>
public sealed class PanelHarness : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly string         _dataDirectory;

    private PanelHarness(WebApplication app, PluginHarness plugins, string dataDirectory, Uri baseAddress)
    {
        _app           = app;
        _dataDirectory = dataDirectory;
        Plugins        = plugins;
        BaseAddress    = baseAddress;
    }

    /// <summary>插件运行时与 Sora 连接。</summary>
    public PluginHarness Plugins { get; }

    /// <summary>面板地址。</summary>
    public Uri BaseAddress { get; }

    /// <summary>部署数据目录中的本地配置文件路径。</summary>
    public string LocalConfigPath => Path.Combine(_dataDirectory, DeploymentPaths.LOCAL_CONFIG_FILE_NAME);

    /// <summary>当前面板 Token。</summary>
    public string Token => new LocalConfigFile(Paths(_dataDirectory)).ReadString("Shinano", "Web", "Token") ?? string.Empty;

    /// <summary>启动面板与插件运行时。</summary>
    public static async Task<PanelHarness> StartAsync(
        HostInfrastructure infrastructure,
        string             bucketPrefix,
        string             pluginDirectory,
        params string[]    pluginIds)
    {
        string dataDirectory = Directory.CreateTempSubdirectory("shinano-panel-").FullName;
        PluginRuntime? runtime = null;
        BotAccessor? bot = null;

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddRoutingCore();
        builder.Services.AddPanel(Paths(dataDirectory));
        // SAFE: 插件运行时与连接在应用构建后、第一次请求之前创建。
        builder.Services.AddSingleton(_ => runtime!);
        builder.Services.AddSingleton(_ => bot!);
        builder.Services.AddSingleton(infrastructure);
        builder.Services.AddSingleton(new RuntimeHealth(TimeProvider.System));
        builder.Services.AddSingleton(LogFileLayout.Create(dataDirectory, DateTime.Now));
        builder.Services.AddSingleton(new LoggingOptions());
        builder.Services.AddSingleton(new MemoryLogSink());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new RestartOptions());
        // 插件包暂存与重启只作用于临时数据目录内的插件目录，不触及仓库中的测试插件输出。
        builder.Services.AddSingleton(new PluginRuntimeSettings
        {
            DataDirectory   = dataDirectory,
            PluginDirectory = "plugins",
            BucketPrefix    = bucketPrefix
        });
        builder.Services.AddSingleton<PluginPackageStaging>();
        builder.Services.AddSingleton(sp => new SuperUserNotifier(
                                          sp.GetRequiredService<BotAccessor>(),
                                          new DeploymentOptions(),
                                          TestRendering.Images,
                                          TimeProvider.System,
                                          sp.GetRequiredService<ILogger<SuperUserNotifier>>()));
        builder.Services.AddSingleton<RestartCoordinator>();
        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapApiRoutes();

        PluginHarness plugins = await PluginHarness.StartAsync(
            infrastructure,
            bucketPrefix,
            pluginDirectory,
            app.Services.GetRequiredService<PluginEndpointDataSource>(),
            pluginIds);
        runtime = plugins.Runtime;
        bot     = plugins.Bot;
        app.Services.GetRequiredService<PanelTokenStore>();
        await app.StartAsync();

        IServerAddressesFeature addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
                                            ?? throw new InvalidOperationException("无法取得监听地址");
        return new PanelHarness(app, plugins, dataDirectory, new Uri(addresses.Addresses.First()));
    }

    /// <summary>创建带独立 Cookie 容器的客户端。</summary>
    public HttpClient CreateClient() =>
        new(new HttpClientHandler { CookieContainer = new CookieContainer() })
        {
            BaseAddress = BaseAddress
        };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        await Plugins.DisposeAsync();
        Directory.Delete(_dataDirectory, recursive: true);
    }

    private static DeploymentPaths Paths(string dataDirectory) =>
        new()
        {
            BaseDirectory = dataDirectory,
            DataDirectory = dataDirectory
        };
}
