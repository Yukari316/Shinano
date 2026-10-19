using Shinano.Framework.Plugins;

namespace Shinano.Host.Plugins;

/// <summary>插件配置的当前值，由 <see cref="PluginConfigStore" /> 读取与更新。</summary>
internal sealed class PluginConfig<T> : IPluginConfig<T> where T : class
{
    private volatile T _value;

    /// <summary>以初始值创建。</summary>
    public PluginConfig(T value)
    {
        _value = value;
    }

    /// <inheritdoc />
    public T Value => _value;

    /// <summary>替换当前值。</summary>
    internal void Replace(T value) => _value = value;
}
