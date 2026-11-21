using Autofac;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Gemini.Api;
using Shinano.Plugin.Gemini.Chat;
using Shinano.Plugin.Gemini.Panel;
using Shinano.Plugin.Gemini.Persistence;
using Sora.Entities.Events;

namespace Shinano.Plugin.Gemini;

/// <summary>Gemini 对话插件入口。</summary>
public sealed class GeminiPlugin : IShinanoPlugin
{
    private const string PAGE_TITLE        = "Gemini 对话设置";
    private const string PAGE_ENTRY        = "index.js";
    private const string SESSION_PURGE_JOB = "清理闲置对话上下文";

    // 闲置判断在每次提问时即时进行，此任务只负责及时释放闲置会话占用的内存（含历史图片）。
    private static readonly TimeSpan SessionPurgeInterval = TimeSpan.FromMinutes(5);

    private GeminiScopeStore?            _scope;
    private GeminiModelSettingsStore?    _modelSettings;
    private GeminiUsageStore?            _usage;
    private GeminiMessageHandler?        _handler;
    private GeminiPanelApi?              _panelApi;
    private GeminiConversationStore?     _conversations;
    private IPluginConfig<GeminiConfig>? _config;

    /// <inheritdoc />
    public void ConfigureServices(ContainerBuilder builder)
    {
        builder.Register(context => context.Resolve<IPluginInitializationContext>().Config<GeminiConfig>())
               .As<IPluginConfig<GeminiConfig>>()
               .SingleInstance();
        builder.Register(context => context.Resolve<IPluginInitializationContext>().Secrets<GeminiSecrets>())
               .As<IPluginSecrets<GeminiSecrets>>()
               .SingleInstance();
        builder.Register(context => context.Resolve<IPluginInitializationContext>().Bot)
               .As<IBotAccessor>()
               .SingleInstance();
        builder.Register(context => context.Resolve<IPluginInitializationContext>().Images)
               .As<IPluginImages>()
               .SingleInstance();

        builder.RegisterInstance(TimeProvider.System).As<TimeProvider>();
        builder.RegisterType<GeminiScopeStore>().SingleInstance();
        builder.RegisterType<GeminiModelSettingsStore>().SingleInstance();
        builder.RegisterType<GeminiUsageStore>().SingleInstance();
        builder.RegisterType<GeminiApiClient>().SingleInstance();
        builder.RegisterType<GeminiCooldownTracker>().SingleInstance();
        builder.RegisterType<GeminiInputReader>().SingleInstance();
        builder.RegisterType<GeminiConversationStore>().SingleInstance();
        builder.RegisterType<GeminiMessageHandler>().SingleInstance();
        builder.RegisterType<GeminiPanelApi>().SingleInstance();

        builder.RegisterBuildCallback(scope =>
        {
            _scope         = scope.Resolve<GeminiScopeStore>();
            _modelSettings = scope.Resolve<GeminiModelSettingsStore>();
            _usage         = scope.Resolve<GeminiUsageStore>();
            _handler       = scope.Resolve<GeminiMessageHandler>();
            _panelApi      = scope.Resolve<GeminiPanelApi>();
            _conversations = scope.Resolve<GeminiConversationStore>();
            _config        = scope.Resolve<IPluginConfig<GeminiConfig>>();
        });
    }

    /// <inheritdoc />
    public void ConfigurePanel(IPanelRegistration panel)
    {
        panel.UseConfig<GeminiConfig>();
        panel.UseSecrets<GeminiSecrets>();
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync(IPluginInitializationContext context, CancellationToken cancellationToken)
    {
        // SAFE: ConfigureServices 阶段已由 Autofac BuildCallback 解析并赋值。
        await _scope!.LoadAsync();
        await _modelSettings!.LoadAsync();
        await _usage!.LoadAsync();
        context.Events.Subscribe<MessageReceivedEvent>(_handler!.HandleAsync);
        context.Scheduler.Register(SESSION_PURGE_JOB, SessionPurgeInterval, PurgeSessionsAsync, runImmediately: false);
        context.Panel.UsePage(PAGE_TITLE, PAGE_ENTRY);
        context.Panel.MapApi(_panelApi!.Map);
    }

    private Task PurgeSessionsAsync(CancellationToken cancellationToken)
    {
        // SAFE: 任务在 InitializeAsync 中登记，此前 BuildCallback 已赋值。
        _conversations!.PurgeExpired(TimeSpan.FromMinutes(_config!.Value.ConversationTimeoutMinutes));
        return Task.CompletedTask;
    }
}
