using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shinano.Tool.Deployment;

/// <summary><c>deployment.json</c> 的读写。</summary>
public static class DeploymentStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    /// <summary>读取部署状态；尚未初始化时为 null。</summary>
    public static DeploymentState? Read(DeploymentLayout layout) =>
        File.Exists(layout.StatePath)
            ? JsonSerializer.Deserialize<DeploymentState>(File.ReadAllText(layout.StatePath), JsonOptions)
            : null;

    /// <summary>先写临时文件再替换，中途失败不会留下半个文件。</summary>
    public static void Write(DeploymentLayout layout, DeploymentState state)
    {
        Directory.CreateDirectory(layout.DataDirectory);
        string temporary = layout.StatePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporary, layout.StatePath, overwrite: true);
    }
}
