using Serilog;
using Shinano.Host;
using Shinano.Host.Archive;
using Shinano.Host.Bot;
using Shinano.Host.Configuration;
using Shinano.Host.Plugins;
using Shinano.Host.Web;
using Shinano.Infrastructure;
using Shinano.Infrastructure.ChatArchive;
using Shinano.Infrastructure.Configuration;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Logging;
using Shinano.Infrastructure.Media;
using Shinano.Infrastructure.Scheduling;
using Shinano.Infrastructure.Storage;
using ILogger = Microsoft.Extensions.Logging.ILogger;

DateTime        startTime = DateTime.Now;
DeploymentPaths paths     = DeploymentPaths.Resolve();

WebApplicationBuilder builder = WebApplication.CreateBuilder(
    new WebApplicationOptions
    {
        Args            = args,
        ContentRootPath = paths.BaseDirectory
    });
builder.Configuration.AddJsonFile(paths.LocalConfigPath, optional: true, reloadOnChange: false);

LoggingOptions loggingOptions = builder.Configuration.GetSection(LoggingOptions.SECTION_NAME).Get<LoggingOptions>()
                                ?? new LoggingOptions();
int configuredMaxLogSize = loggingOptions.MaxTotalSizeMiB;
if (configuredMaxLogSize <= 0) loggingOptions.MaxTotalSizeMiB = new LoggingOptions().MaxTotalSizeMiB;
using ShinanoLogging logging = ShinanoLogging.Create(loggingOptions, paths.BaseDirectory, startTime);
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(logging.Logger);

WebOptions webOptions = builder.Configuration.GetSection(WebOptions.SECTION_NAME).Get<WebOptions>() ?? new WebOptions();
WebEndpoint endpoint = WebEndpointResolver.Resolve(webOptions, paths.DataDirectory);
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Listen(
        endpoint.Address,
        endpoint.Port,
        listen =>
        {
            if (endpoint.Certificate is not null) listen.UseHttps(endpoint.Certificate);
        });
});

builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(loggingOptions);
builder.Services.AddSingleton(logging.Layout);
builder.Services.AddSingleton(logging.MemorySink);
builder.Services.AddSingleton(sp => new LogDirectoryCleaner(
                                  logging.Layout,
                                  loggingOptions.MaxTotalSizeMiB,
                                  sp.GetRequiredService<ILoggerFactory>().CreateLogger<LogDirectoryCleaner>()));
builder.Services.AddHostedService<LogRetentionService>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RuntimeHealth>();
builder.Services.AddSingleton<HostScheduler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HostScheduler>());
DeploymentOptions deployment = builder.Bind<DeploymentOptions>(DeploymentOptions.SECTION_NAME);
builder.Services.AddSingleton(deployment);
builder.Services.AddSingleton(new HostInfrastructureSettings
{
    Deployment  = deployment,
    Database    = builder.Bind<DatabaseOptions>(DatabaseOptions.SECTION_NAME),
    RustFs      = builder.Bind<RustFsOptions>(RustFsOptions.SECTION_NAME),
    Media       = builder.Bind<MediaOptions>(MediaOptions.SECTION_NAME),
    ChatArchive = builder.Bind<ChatArchiveOptions>(ChatArchiveOptions.SECTION_NAME)
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<HostInfrastructureSettings>().ChatArchive);
builder.Services.AddSingleton<HostInfrastructure>();
builder.Services.AddHostedService<HostInfrastructureStartupService>();

builder.Services.AddSingleton(builder.Bind<LlBotOptions>(LlBotOptions.SECTION_NAME));
builder.Services.AddSingleton<BotAccessor>();
builder.Services.AddSingleton(new PluginRuntimeSettings
{
    DataDirectory   = paths.DataDirectory,
    PluginDirectory = builder.Bind<PluginOptions>(PluginOptions.SECTION_NAME).Directory,
    BucketPrefix    = deployment.BucketPrefix
});
builder.Services.AddSingleton<PluginRuntime>();
builder.Services.AddSingleton<SuperUserNotifier>();
builder.Services.AddSingleton<ArchiveGapTracker>();
builder.Services.AddHostedService<HealthReactions>();
// 归档服务在 BotService 之前登记：托管服务按相反顺序停止，Sora 排空记录队列后归档服务才停止。
builder.Services.AddSingleton<ChatArchiveService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ChatArchiveService>());
builder.Services.AddHostedService<BotService>();

WebApplication app = builder.Build();

ILogger startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Shinano.Startup");
startupLogger.LogInformation("部署数据目录：{DataDirectory}", paths.DataDirectory);
if (configuredMaxLogSize <= 0)
    startupLogger.LogError(
        "日志目录大小上限 {Configured} MiB 无效，改用 {Fallback} MiB",
        configuredMaxLogSize,
        loggingOptions.MaxTotalSizeMiB);
foreach (string problem in endpoint.Problems)
    startupLogger.LogError("管理面板监听配置无效：{Problem}", problem);
startupLogger.LogInformation(
    "管理面板监听 {Scheme}://{Address}:{Port}",
    endpoint.Certificate is null ? "http" : "https",
    endpoint.Address,
    endpoint.Port);

app.UseDefaultFiles();
app.UseStaticFiles();

await app.RunAsync();
