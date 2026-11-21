using Serilog;
using Shinano.Monitor.Probing;
using Shinano.Monitor.Web;
using System.Text.Json.Serialization;
using ILogger = Microsoft.Extensions.Logging.ILogger;

const string DATA_DIRECTORY = "data";

string baseDirectory = AppContext.BaseDirectory;
WebApplicationBuilder builder = WebApplication.CreateBuilder(
    new WebApplicationOptions
    {
        Args            = args,
        ContentRootPath = baseDirectory
    });
MonitorSettings settings = MonitorSettings.Load(baseDirectory);

using MonitorLogging logging = MonitorLogging.Create(baseDirectory);
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(logging.Logger);

ListenEndpoint endpoint = ListenEndpoint.Resolve(settings.Listen, MonitorSettings.DEFAULT_PORT, settings.DataDirectory);
builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.Listen(
        endpoint.Address,
        endpoint.Port,
        listen =>
        {
            if (endpoint.Certificate is not null) listen.UseHttps(endpoint.Certificate);
        }));

// 只在由 Windows 服务或 systemd 启动时生效。
builder.Services.AddWindowsService(options => options.ServiceName = settings.ServiceName);
builder.Services.AddSystemd();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new ProbeStore(
                                  Path.Combine(baseDirectory, DATA_DIRECTORY),
                                  settings.TimeZone,
                                  sp.GetRequiredService<ILogger<ProbeStore>>()));
builder.Services.AddSingleton(sp => new LogRetention(
                                  logging.Directory,
                                  logging.RunPrefix,
                                  settings.MaxLogSizeMiB,
                                  sp.GetRequiredService<ILogger<LogRetention>>()));
builder.Services.AddHostedService<ProbeService>();

WebApplication app = builder.Build();

ILogger startup = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Shinano.Monitor.Startup");
if (settings.ConfigPath is null)
    startup.LogError(
        "找不到部署配置文件：请设置环境变量 {Variable} 指向部署数据目录，各项探测将显示为未配置",
        MonitorSettings.DATA_DIRECTORY_VARIABLE);
else
    startup.LogInformation("部署配置文件：{ConfigPath}", settings.ConfigPath);
foreach (string problem in endpoint.Problems)
    startup.LogError("状态页监听配置无效：{Problem}", problem);
startup.LogInformation("状态页监听 {BaseUrl}，探测间隔 {Interval} 秒", endpoint.BaseUrl, settings.Interval.TotalSeconds);

ProbeStore store = app.Services.GetRequiredService<ProbeStore>();
store.Load(DateTimeOffset.UtcNow);
app.Services.GetRequiredService<LogRetention>().Clean();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapMonitorRoutes();

await app.RunAsync();
