using Shinano.Host.Configuration;
using System.Text.Json.Serialization;

namespace Shinano.Host.Panel;

/// <summary>管理面板的服务登记。</summary>
public static class PanelServices
{
    /// <summary>登记面板认证、Token、配置编辑、外观与插件 API 端点来源；JSON 响应中的枚举以名称表示。</summary>
    public static IServiceCollection AddPanel(this IServiceCollection services, DeploymentPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton<LocalConfigFile>();
        services.AddSingleton<PanelTokenStore>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<HostConfigConnectionCheck>();
        services.AddSingleton<HostConfigEditor>();
        services.AddSingleton<AppearanceStore>();
        services.AddSingleton(sp => new PluginEndpointDataSource(sp));
        services.AddPanelAuthentication(paths.DataDirectory);
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }
}
