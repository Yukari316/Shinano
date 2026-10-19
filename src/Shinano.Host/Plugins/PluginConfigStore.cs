using System.Text.Json;
using Shinano.Infrastructure.Database;

namespace Shinano.Host.Plugins;

/// <summary>
/// 插件普通配置的读写，存于全局 schema 的 <c>plugin_config</c>。没有记录时使用配置类型的无参构造结果。
/// </summary>
public sealed class PluginConfigStore
{
    /// <summary>配置 JSON 的序列化规则：属性名使用驼峰。</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ShinanoDatabase _database;

    /// <summary>创建存储。</summary>
    public PluginConfigStore(ShinanoDatabase database)
    {
        _database = database;
    }

    /// <summary>读取插件配置；记录无法解析为 <typeparamref name="T" /> 时抛出异常。</summary>
    /// <exception cref="JsonException">已保存的配置与当前配置类型不兼容。</exception>
    public T Load<T>(string pluginId) where T : class, new()
    {
        string? json = _database.Global.Queryable<PluginConfigRecord>()
                                .Where(record => record.PluginId == pluginId)
                                .Select(record => record.Config)
                                .First();
        return json is null ? new T() : JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
    }
}
