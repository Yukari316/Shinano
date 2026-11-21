using System.Text.Json;
using System.Text.Json.Nodes;
using Shinano.Tool.Cli;

namespace Shinano.Tool.Configuration;

/// <summary>
/// 部署数据目录中部署配置文件（含敏感配置）的读写。只修改给出的配置项，保留其余内容；先写临时文件再替换，
/// 中途失败不会留下半个文件。路径格式为 <c>Shinano:Web:Port</c>。
/// </summary>
public sealed class DeploymentConfig
{
    private const char PATH_SEPARATOR = ':';

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string     _path;
    private readonly JsonObject _root;

    private DeploymentConfig(string path, JsonObject root)
    {
        _path = path;
        _root = root;
    }

    /// <summary>读取部署配置；文件不存在时为空配置。</summary>
    /// <exception cref="ToolException">文件不是有效的 JSON 对象。</exception>
    public static DeploymentConfig Load(string path)
    {
        if (!File.Exists(path)) return new DeploymentConfig(path, []);
        try
        {
            return new DeploymentConfig(path, JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? []);
        }
        catch (JsonException exception)
        {
            throw new ToolException($"部署配置文件不是有效的 JSON：{path}：{exception.Message}");
        }
    }

    /// <summary>字符串值；不存在或为空时为 null。</summary>
    public string? GetString(string path) =>
        Find(path) is JsonValue value && value.TryGetValue(out string? text) && text.Length > 0 ? text : null;

    /// <summary>整数值；不存在或不是整数时为 null。</summary>
    public int? GetInt(string path) =>
        Find(path) switch
        {
            JsonValue value when value.TryGetValue(out int number)                                     => number,
            JsonValue value when value.TryGetValue(out string? text) && int.TryParse(text, out int parsed) => parsed,
            _                                                                                          => null
        };

    /// <summary>整数数组；不存在时为空。</summary>
    public long[] GetLongArray(string path) =>
        Find(path) is JsonArray array
            ? [.. array.OfType<JsonValue>().Select(item => item.TryGetValue(out long number) ? number : 0).Where(number => number > 0)]
            : [];

    /// <summary>设置配置项，按需创建配置节。</summary>
    public void Set(string path, JsonNode? value)
    {
        string[] keys = path.Split(PATH_SEPARATOR);
        JsonObject current = _root;
        foreach (string key in keys[..^1])
        {
            if (current[key] is not JsonObject next)
            {
                next = [];
                current[key] = next;
            }

            current = next;
        }

        current[keys[^1]] = value;
    }

    /// <summary>删除配置项；不存在时不做任何事。</summary>
    public void Remove(string path)
    {
        string[] keys = path.Split(PATH_SEPARATOR);
        if (Find(string.Join(PATH_SEPARATOR, keys[..^1])) is JsonObject parent) parent.Remove(keys[^1]);
    }

    /// <summary>写回文件。</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        PrivateFile.Write(_path, _root.ToJsonString(WriteOptions));
    }

    private JsonNode? Find(string path)
    {
        JsonNode? node = _root;
        foreach (string key in path.Split(PATH_SEPARATOR))
            node = node is JsonObject json ? json[key] : null;
        return node;
    }
}
