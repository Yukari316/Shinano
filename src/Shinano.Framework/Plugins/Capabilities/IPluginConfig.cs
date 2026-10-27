namespace Shinano.Framework.Plugins.Capabilities;

/// <summary>插件配置的当前值。配置在管理面板修改后，<see cref="Value" /> 返回新的实例。</summary>
/// <typeparam name="T">配置类型，无参构造结果即默认配置。</typeparam>
public interface IPluginConfig<out T> where T : class
{
    /// <summary>当前配置。调用方不应修改返回的实例。</summary>
    T Value { get; }
}
