using Shinano.Host.Plugins.Capabilities;

namespace Shinano.Host.Plugins.Config;

/// <summary>不依赖机密类型参数访问 <see cref="PluginSecrets{T}" />，供面板读取状态与写入机密。</summary>
internal interface IPluginSecretsHolder
{
    /// <summary>当前机密。</summary>
    object Current { get; }

    /// <summary>
    /// 在写入锁内读取当前机密、套用修改并写入文件，避免覆盖插件同时写入的新值（如刚刷新的登录状态）。
    /// <paramref name="apply" /> 返回 null 时不写入；返回值的类型须与声明的机密类型一致。
    /// </summary>
    /// <returns>是否已写入。</returns>
    Task<bool> UpdateAsync(Func<object, object?> apply, CancellationToken cancellationToken);
}
