using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shinano.Host.Plugins.Models;

namespace Shinano.Host.Plugins;

/// <summary>
/// 插件机密表单：按机密类型的公共可读写属性列出各项状态，并把面板提交的修改应用到当前机密的副本上。
/// 只有字符串属性可写；值从不返回给面板。
/// </summary>
public static class PluginSecretsSchema
{
    // 与 PluginSecrets 读写文件的选项一致，副本经同一套序列化往返得到。
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>列出机密各项及是否已配置。</summary>
    public static IReadOnlyList<PluginSecretField> Describe(Type secretsType, object current) =>
    [
        .. PropertiesOf(secretsType).Select(property =>
        {
            DisplayAttribute? display = property.GetCustomAttribute<DisplayAttribute>();
            return new PluginSecretField
            {
                Key         = property.Name,
                Label       = display?.GetName() ?? property.Name,
                Description = display?.GetDescription() ?? string.Empty,
                Editable    = property.PropertyType == typeof(string),
                Configured  = IsConfigured(property.GetValue(current))
            };
        })
    ];

    /// <summary>
    /// 把 <paramref name="changes" /> 应用到当前机密的副本。键为属性名，值为非空字符串表示替换，<c>null</c> 表示清除。
    /// </summary>
    /// <param name="errors">校验错误；有错误时返回 null。</param>
    public static object? Apply(Type secretsType, object current, JsonObject changes, out IReadOnlyList<string> errors)
    {
        List<string> problems = [];
        Dictionary<string, PropertyInfo> properties = PropertiesOf(secretsType).ToDictionary(property => property.Name);
        List<(PropertyInfo Property, string? Value)> accepted = [];
        foreach ((string key, JsonNode? value) in changes)
        {
            if (!properties.TryGetValue(key, out PropertyInfo? property) || property.PropertyType != typeof(string))
                problems.Add($"{key} 不能在面板中修改");
            else if (value is null) accepted.Add((property, null));
            else if (value is JsonValue text && text.TryGetValue(out string? content) && content.Length > 0) accepted.Add((property, content));
            else problems.Add($"{key} 必须是非空字符串");
        }

        errors = problems;
        if (problems.Count > 0) return null;

        // SAFE: 当前机密可序列化，往返得到同类型的非空副本。
        object copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(current, secretsType, JsonOptions), secretsType, JsonOptions)!;
        foreach ((PropertyInfo property, string? value) in accepted)
            property.SetValue(copy, value);
        return copy;
    }

    private static IEnumerable<PropertyInfo> PropertiesOf(Type secretsType) =>
        secretsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                   .Where(property => property is { CanRead: true, CanWrite: true })
                   .OrderBy(property => property.GetCustomAttribute<DisplayAttribute>()?.GetOrder() ?? int.MaxValue);

    private static bool IsConfigured(object? value) =>
        value switch
        {
            null          => false,
            string text   => text.Length > 0,
            ValueType     => !value.Equals(Activator.CreateInstance(value.GetType())),
            _             => true
        };
}
