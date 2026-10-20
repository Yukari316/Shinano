namespace Shinano.Host.Plugins;

/// <summary>不依赖配置类型参数访问 <see cref="PluginConfig{T}" />，供面板读取与保存配置。</summary>
internal interface IPluginConfigHolder
{
    /// <summary>当前配置。</summary>
    object Value { get; }

    /// <summary>替换当前配置，调用方保证类型与声明的配置类型一致。</summary>
    void Replace(object value);
}
