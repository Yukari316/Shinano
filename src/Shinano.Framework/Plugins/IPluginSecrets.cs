namespace Shinano.Framework.Plugins;

/// <summary>
/// 插件机密，存于部署数据目录的 <c>secrets/&lt;plugin-id&gt;.json</c>。管理员可直接编辑该文件，
/// 插件也可写回（如扫码登录结果）；文件不存在时为默认值。
/// </summary>
/// <typeparam name="T">机密类型，无参构造结果即默认值。</typeparam>
public interface IPluginSecrets<T> where T : class
{
    /// <summary>当前机密。调用方不应修改返回的实例。</summary>
    T Value { get; }

    /// <summary>以 <paramref name="value" /> 整体替换机密并写入文件。</summary>
    Task SaveAsync(T value, CancellationToken cancellationToken);
}
