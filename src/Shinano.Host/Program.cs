using Serilog;
using Shinano.Framework.Plugins;
using Shinano.Host.Bot;
using Shinano.Host.Configuration;
using Shinano.Host.Panel;
using Shinano.Host.Panel.Auth;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Purge;
using Shinano.Host.Plugins.Staging;
using Shinano.Host.Rendering;
using Shinano.Host.Restart;
using Shinano.Host.Tools;
using Shinano.Host.Web;
using Shinano.Infrastructure.ChatArchive;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Logging;
using Shinano.Infrastructure.Media;
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
// 部署配置是唯一配置来源：去掉默认的 appsettings、环境变量与命令行，未写出的项取 Options 类的默认值。
builder.Configuration.Sources.Clear();
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

RestartOptions restartOptions = builder.Configuration.GetSection(RestartOptions.SECTION_NAME).Get<RestartOptions>() ?? new RestartOptions();
// 只在由 Windows 服务或 systemd 启动时生效，直接运行时不改变行为。
builder.Services.AddWindowsService(options => options.ServiceName = restartOptions.EffectiveServiceName);
builder.Services.AddSystemd();
builder.Services.AddSingleton(restartOptions);

builder.Services.AddPanel(paths);
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
builder.Services.AddSingleton<AccountActivation>();
builder.Services.AddSingleton<PluginPackageStaging>();
builder.Services.AddSingleton<RestartCoordinator>();
HostFonts fonts = new(AppContext.BaseDirectory);
builder.Services.AddSingleton(fonts);
builder.Services.AddSingleton<IPluginFonts>(fonts);
builder.Services.AddSingleton(builder.Bind<RenderingOptions>(RenderingOptions.SECTION_NAME));
builder.Services.AddSingleton<CardRenderer>();
builder.Services.AddSingleton(sp => new SuperUserNotifier(
                                  sp.GetRequiredService<BotAccessor>(),
                                  deployment,
                                  sp.GetRequiredService<CardRenderer>(),
                                  TimeProvider.System,
                                  sp.GetRequiredService<ILogger<SuperUserNotifier>>()));
builder.Services.AddSingleton(sp => new BrowserService(
                                  builder.Bind<BrowserOptions>(BrowserOptions.SECTION_NAME),
                                  paths.DataDirectory,
                                  sp.GetRequiredService<TimeProvider>(),
                                  sp.GetRequiredService<ILogger<BrowserService>>()));
builder.Services.AddSingleton(sp => new FfmpegService(
                                  builder.Bind<FfmpegOptions>(FfmpegOptions.SECTION_NAME),
                                  paths.DataDirectory,
                                  sp.GetRequiredService<ILogger<FfmpegService>>()));
builder.Services.AddSingleton<PluginDataPurger>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PluginDataPurger>());
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
if (fonts.LoadError is not null)
    startupLogger.LogError("{Error}；Super User 通知改为文字，插件的长图与自绘不可用", fonts.LoadError);
startupLogger.LogInformation(
    "管理面板监听 {Scheme}://{Address}:{Port}",
    endpoint.Certificate is null ? "http" : "https",
    endpoint.Address,
    endpoint.Port);

// 首次启动时生成面板 Token，避免等到第一次登录请求。
app.Services.GetRequiredService<PanelTokenStore>();
app.UseDefaultFiles();
app.UseStaticFiles(PanelStaticFiles.Options);
app.UseAuthentication();
app.UseAuthorization();
app.MapApiRoutes();

// 托管服务启动前应用或还原插件包变更：插件程序集加载后其目录会被锁定。
app.Services.GetRequiredService<RestartCoordinator>().PrepareStartup();
await app.RunAsync();
fonts.Dispose();
