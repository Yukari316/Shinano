using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shinano.Host.Restart;

/// <summary>部署数据目录中重启与插件包状态文件的读写，枚举以名称保存。</summary>
internal static class StateFile
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    /// <summary>读取状态文件；文件不存在时为 null。</summary>
    public static T? Read<T>(string path) where T : class =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : null;

    /// <summary>先写临时文件再替换，进程中途退出时不会留下写了一半的状态。</summary>
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>删除状态文件；不存在时不做任何事。</summary>
    public static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
