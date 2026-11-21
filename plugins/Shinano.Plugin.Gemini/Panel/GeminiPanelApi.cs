using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Gemini.Api;
using Shinano.Plugin.Gemini.Persistence;

namespace Shinano.Plugin.Gemini.Panel;

/// <summary>Gemini 专有面板的管理 API 路由登记，前缀由宿主规定为 <c>/api/plugin-api/gemini</c>。</summary>
public sealed class GeminiPanelApi
{
    private readonly GeminiScopeStore              _scope;
    private readonly GeminiModelSettingsStore      _modelSettings;
    private readonly GeminiUsageStore              _usage;
    private readonly GeminiApiClient               _api;
    private readonly IPluginConfig<GeminiConfig>   _config;
    private readonly IPluginSecrets<GeminiSecrets> _secrets;
    private readonly IBotAccessor                  _bot;

    /// <summary>创建路由登记。</summary>
    public GeminiPanelApi(
        GeminiScopeStore              scope,
        GeminiModelSettingsStore      modelSettings,
        GeminiUsageStore              usage,
        GeminiApiClient               api,
        IPluginConfig<GeminiConfig>   config,
        IPluginSecrets<GeminiSecrets> secrets,
        IBotAccessor                  bot)
    {
        _scope         = scope;
        _modelSettings = modelSettings;
        _usage         = usage;
        _api           = api;
        _config        = config;
        _secrets       = secrets;
        _bot           = bot;
    }

    /// <summary>登记全部路由。</summary>
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/scope", () => GeminiPanelEndpoints.Scopes(_scope));
        routes.MapPut("/scope/{kind}", (string kind, GeminiScopeUpdate update) => GeminiPanelEndpoints.SaveScopeAsync(_scope, kind, update));
        routes.MapGet(
            "/scope/{kind}/candidates",
            (string kind, CancellationToken cancellationToken) => GeminiPanelEndpoints.CandidatesAsync(_bot, _scope, kind, cancellationToken));
        routes.MapGet("/model", () => GeminiPanelEndpoints.GetModelSettings(_modelSettings, _secrets));
        routes.MapPut("/model", (GeminiModelSettingsUpdate update) => GeminiPanelEndpoints.SaveModelSettingsAsync(_modelSettings, _secrets, update));
        routes.MapPut("/system-prompt", (GeminiSystemPromptUpdate update) => GeminiPanelEndpoints.SaveSystemPromptAsync(_modelSettings, _secrets, update));
        routes.MapGet("/models", (CancellationToken cancellationToken) => GeminiPanelEndpoints.ListModelsAsync(_api, _config, _secrets, cancellationToken));
        routes.MapGet("/usage", (int? days) => GeminiPanelEndpoints.UsageAsync(_usage, days));
    }
}
