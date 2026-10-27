using Shinano.Framework.Plugins.Capabilities;
using Shinano.Host.Plugins.Config;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>插件配置的当前值，由 <see cref="PluginConfigStore" /> 读取与更新。</summary>
internal sealed class PluginConfig<T> : IPluginConfig<T>, IPluginConfigHolder where T : class
{
    private volatile T _value;

    /// <summary>以初始值创建。</summary>
    public PluginConfig(T value)
    {
        _value = value;
    }

    /// <inheritdoc />
    public T Value => _value;

    /// <inheritdoc />
    object IPluginConfigHolder.Value => _value;

    /// <inheritdoc />
    public void Replace(object value) => _value = (T)value;
}
