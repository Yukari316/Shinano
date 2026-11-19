using Shinano.Infrastructure.Database;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Shinano.Host.Plugins.Config;

/// <summary>
/// 插件普通配置的读写，存于全局 schema 的 <c>plugin_config</c>。没有记录时使用配置类型的无参构造结果。
/// </summary>
public sealed class PluginConfigStore
{
    /// <summary>
    /// 配置 JSON 的序列化规则：属性名使用驼峰，枚举以名称表示；数字只接受 JSON 数字，
    /// 使导出的 JSON Schema 中数字字段的类型保持为数字。
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling   = JsonNumberHandling.Strict,
        Converters       = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private readonly ShinanoDatabase _database;

    /// <summary>创建存储。</summary>
    public PluginConfigStore(ShinanoDatabase database)
    {
        _database = database;
    }

    /// <summary>读取插件配置；没有记录时返回 <paramref name="configType" /> 的无参构造结果。</summary>
    /// <exception cref="JsonException">已保存的配置与当前配置类型不兼容。</exception>
    public object Load(string pluginId, Type configType)
    {
        string? json = _database.Global.Queryable<PluginConfigRecord>()
                                .Where(record => record.PluginId == pluginId)
                                .Select(record => record.Config)
                                .First();
        return (json is null ? null : JsonSerializer.Deserialize(json, configType, JsonOptions))
               ?? Activator.CreateInstance(configType)
               ?? throw new InvalidOperationException($"无法创建配置类型 {configType}");
    }

    /// <summary>保存插件配置，覆盖已有记录。</summary>
    public async Task SaveAsync(string pluginId, Type configType, object config, CancellationToken cancellationToken) =>
        await _database.Global.Storageable(
                           new PluginConfigRecord
                           {
                               PluginId  = pluginId,
                               Config    = JsonSerializer.Serialize(config, configType, JsonOptions),
                               UpdatedAt = DateTimeOffset.UtcNow
                           })
                       .ExecuteCommandAsync(cancellationToken);
}
