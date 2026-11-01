using Shinano.Framework.Plugins;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>收集插件在加载时声明的面板内容。</summary>
internal sealed class PluginPanelRegistration : IPanelRegistration
{
    /// <summary>声明的配置类型；未声明时为 null。</summary>
    public Type? ConfigType { get; private set; }

    /// <summary>声明的机密类型；未声明时为 null。</summary>
    public Type? SecretsType { get; private set; }

    /// <inheritdoc />
    public void UseConfig<TConfig>() where TConfig : class, new()
    {
        if (ConfigType is not null) throw new InvalidOperationException("每个插件只能声明一个配置类型");
        ConfigType = typeof(TConfig);
    }

    /// <inheritdoc />
    public void UseSecrets<TSecrets>() where TSecrets : class, new()
    {
        if (SecretsType is not null) throw new InvalidOperationException("每个插件只能声明一个机密类型");
        SecretsType = typeof(TSecrets);
    }
}
