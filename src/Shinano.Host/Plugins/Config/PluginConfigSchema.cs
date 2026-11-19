using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Shinano.Host.Plugins.Config;

/// <summary>
/// 由插件配置类型生成面板通用表单使用的 JSON Schema，并校验提交的配置。
/// </summary>
/// <remarks>
/// 属性的 <c>[Display]</c> 写入 <c>title</c>（Name）、<c>description</c>（Description）与 <c>x-order</c>（Order）；
/// <c>[Range]</c>、<c>[StringLength]</c>、<c>[RegularExpression]</c> 写入对应的 JSON Schema 约束，<c>[Required]</c> 写入 <c>required</c>。
/// 校验只覆盖配置对象顶层的 DataAnnotations，嵌套对象的特性只用于前端提示。
/// </remarks>
public static class PluginConfigSchema
{
    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode             = Annotate
    };

    /// <summary>生成配置类型的 JSON Schema。</summary>
    public static JsonNode Create(Type configType) =>
        PluginConfigStore.JsonOptions.GetJsonSchemaAsNode(configType, ExporterOptions);

    /// <summary>把提交的 JSON 反序列化为配置类型并校验。</summary>
    /// <returns>配置对象；校验失败时为 null，并在 <paramref name="errors" /> 中给出原因。</returns>
    public static object? Parse(Type configType, JsonNode? json, out IReadOnlyList<string> errors)
    {
        object? config;
        try
        {
            config = json.Deserialize(configType, PluginConfigStore.JsonOptions);
        }
        catch (JsonException exception)
        {
            errors = [$"配置格式错误：{exception.Message}"];
            return null;
        }

        if (config is null)
        {
            errors = ["配置不能为空"];
            return null;
        }

        List<ValidationResult> results = [];
        if (Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true))
        {
            errors = [];
            return config;
        }

        errors = [.. results.Select(result => result.ErrorMessage ?? "配置无效")];
        return null;
    }

    private static JsonNode Annotate(JsonSchemaExporterContext context, JsonNode schema)
    {
        if (schema is not JsonObject node) return schema;

        if (context.TypeInfo.Kind == JsonTypeInfoKind.Object)
            AddRequired(context, node);

        if (context.PropertyInfo?.AttributeProvider is not ICustomAttributeProvider provider) return node;

        foreach (object attribute in provider.GetCustomAttributes(inherit: true))
        {
            switch (attribute)
            {
                case DisplayAttribute display:
                    if (display.GetName() is { } title) node["title"] = title;
                    if (display.GetDescription() is { } description) node["description"] = description;
                    if (display.GetOrder() is { } order) node["x-order"] = order;
                    break;
                case RangeAttribute range:
                    node["minimum"] = JsonValue.Create(Convert.ToDouble(range.Minimum));
                    node["maximum"] = JsonValue.Create(Convert.ToDouble(range.Maximum));
                    break;
                case StringLengthAttribute length:
                    node["maxLength"] = length.MaximumLength;
                    if (length.MinimumLength > 0) node["minLength"] = length.MinimumLength;
                    break;
                case RegularExpressionAttribute pattern:
                    node["pattern"] = pattern.Pattern;
                    break;
            }
        }

        return node;
    }

    private static void AddRequired(JsonSchemaExporterContext context, JsonObject node)
    {
        string[] required =
        [
            .. context.TypeInfo.Properties
                      .Where(property => property.AttributeProvider?.IsDefined(typeof(RequiredAttribute), inherit: true) == true)
                      .Select(property => property.Name)
        ];
        if (required.Length == 0) return;

        if (node["required"] is not JsonArray existing)
        {
            existing = [];
            node["required"] = existing;
        }

        foreach (string name in required.Where(name => existing.All(item => item?.GetValue<string>() != name)))
            existing.Add(name);
    }
}
