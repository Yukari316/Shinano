using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shinano.Host.Configuration;

/// <summary>
/// 部署数据目录中的本地配置文件（含敏感配置）的读写。写入时保留未修改的配置项，先写临时文件再替换，
/// 避免中途失败留下半个文件。修改在进程重启后由配置系统读取。
/// </summary>
public sealed class LocalConfigFile
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Lock   _lock = new();

    /// <summary>创建读写入口。</summary>
    public LocalConfigFile(DeploymentPaths paths)
    {
        _path = paths.LocalConfigPath;
    }

    /// <summary>读取配置项的字符串值；文件、配置节或配置项不存在时返回 null。</summary>
    /// <param name="path">配置路径，如 <c>["Shinano", "Web", "Token"]</c>。</param>
    public string? ReadString(params string[] path)
    {
        lock (_lock)
        {
            JsonNode? node = ReadRoot();
            foreach (string key in path)
                node = node?[key];
            return node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
        }
    }

    /// <summary>读取—修改—写回配置文件。</summary>
    /// <param name="change">修改根对象；用 <see cref="Section" /> 取得或创建配置节。</param>
    public void Update(Action<JsonObject> change)
    {
        lock (_lock)
        {
            JsonObject root = ReadRoot();
            change(root);
            PrivateFile.Write(_path, root.ToJsonString(WriteOptions));
        }
    }

    /// <summary>取得或创建 <paramref name="root" /> 下按 <paramref name="path" /> 嵌套的配置节。</summary>
    public static JsonObject Section(JsonObject root, params string[] path)
    {
        JsonObject current = root;
        foreach (string key in path)
        {
            if (current[key] is not JsonObject next)
            {
                next = [];
                current[key] = next;
            }

            current = next;
        }

        return current;
    }

    private JsonObject ReadRoot() =>
        File.Exists(_path) ? JsonNode.Parse(File.ReadAllText(_path)) as JsonObject ?? [] : [];
}
