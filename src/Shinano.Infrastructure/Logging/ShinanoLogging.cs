using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Sora.Entities;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Shinano.Infrastructure.Logging;

/// <summary>
/// 进程唯一的日志管线：控制台、<c>Logs</c> 目录文件与内存流共用一个 Serilog 记录器，
/// 同时交给 Sora（<see cref="SoraLogger" />）与 ASP.NET Core 使用。
/// </summary>
public sealed class ShinanoLogging : IDisposable
{
    private const string ASP_NET_CORE_CATEGORY = "Microsoft.AspNetCore";

    private const string OUTPUT_TEMPLATE =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    private ShinanoLogging(Logger logger, LogFileLayout layout, MemoryLogSink memorySink)
    {
        Logger     = logger;
        Layout     = layout;
        MemorySink = memorySink;
    }

    /// <summary>共享的 Serilog 记录器。</summary>
    public Logger Logger { get; }

    /// <summary>本次运行的日志文件布局。</summary>
    public LogFileLayout Layout { get; }

    /// <summary>管理面板实时日志使用的内存流。</summary>
    public MemoryLogSink MemorySink { get; }

    /// <summary>
    /// 创建日志管线并配置 <see cref="SoraLogger" />。每个进程只能调用一次，且必须早于任何 Sora 组件取得记录器。
    /// </summary>
    /// <param name="options">日志配置。</param>
    /// <param name="baseDirectory">程序启动目录，日志写入其下的 <c>Logs</c>。</param>
    /// <param name="startTime">进程启动的本地时间，用于日志文件名。</param>
    /// <exception cref="InvalidOperationException">SoraLogger 已被配置或已被使用。</exception>
    public static ShinanoLogging Create(LoggingOptions options, string baseDirectory, DateTime startTime)
    {
        LogFileLayout layout     = LogFileLayout.Create(baseDirectory, startTime);
        MemoryLogSink memorySink = new();
        LogEventLevel level      = ToSerilogLevel(options.MinimumLevel);

        Logger logger = new LoggerConfiguration()
                        .MinimumLevel.Is(level)
                        // 请求级日志量大，只保留警告以上；高于配置等级时以配置为准。
                        .MinimumLevel.Override(ASP_NET_CORE_CATEGORY, Max(level, LogEventLevel.Warning))
                        .Enrich.FromLogContext()
                        .WriteTo.Console(outputTemplate: OUTPUT_TEMPLATE)
                        .WriteTo.File(
                            layout.FirstFilePath,
                            outputTemplate: OUTPUT_TEMPLATE,
                            fileSizeLimitBytes: LogFileLayout.FILE_SIZE_LIMIT_BYTES,
                            rollOnFileSizeLimit: true,
                            shared: false)
                        .WriteTo.Sink(memorySink)
                        .CreateLogger();

        // 记录器归本类所有；Sora 侧的工厂不释放它。
        SoraLogger.Configure(new SerilogLoggerFactory(logger, dispose: false));
        return new ShinanoLogging(logger, layout, memorySink);
    }

    /// <summary>刷新并关闭全部日志输出。</summary>
    public void Dispose() => Logger.Dispose();

    private static LogEventLevel Max(LogEventLevel left, LogEventLevel right) => left > right ? left : right;

    private static LogEventLevel ToSerilogLevel(MsLogLevel level) =>
        level switch
        {
            MsLogLevel.Trace       => LogEventLevel.Verbose,
            MsLogLevel.Debug       => LogEventLevel.Debug,
            MsLogLevel.Information => LogEventLevel.Information,
            MsLogLevel.Warning     => LogEventLevel.Warning,
            MsLogLevel.Error       => LogEventLevel.Error,
            MsLogLevel.Critical    => LogEventLevel.Fatal,
            // Serilog 没有"关闭"等级；None 退化为只输出 Fatal。
            _ => LogEventLevel.Fatal
        };
}
