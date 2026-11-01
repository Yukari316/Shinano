using Shinano.Host.Plugins.Capabilities;

namespace Shinano.Host.Plugins;

/// <summary>不依赖机密类型参数访问 <see cref="PluginSecrets{T}" />，供面板读取状态与写入机密。</summary>
internal interface IPluginSecretsHolder
{
    /// <summary>当前机密。</summary>
    object Current { get; }

    /// <summary>整体替换机密并写入文件，调用方保证类型与声明的机密类型一致。</summary>
    Task SaveAsync(object value, CancellationToken cancellationToken);
}
