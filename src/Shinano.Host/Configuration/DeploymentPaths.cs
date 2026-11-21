namespace Shinano.Host.Configuration;

/// <summary>
/// 部署相关的文件位置。部署数据目录保存部署配置、插件机密与插件暂存，不进入版本库。
/// </summary>
public sealed class DeploymentPaths
{
    /// <summary>指定部署数据目录的环境变量名；未设置时使用启动目录下的 <c>data</c>。</summary>
    public const string DATA_DIRECTORY_VARIABLE = "SHINANO_DATA_DIR";

    /// <summary>部署配置文件名，位于部署数据目录下，是宿主唯一的配置来源，含敏感配置。</summary>
    public const string CONFIG_FILE_NAME = "shinano.json";

    private const string DEFAULT_DATA_DIRECTORY_NAME = "data";

    /// <summary>程序启动目录。</summary>
    public required string BaseDirectory { get; init; }

    /// <summary>部署数据目录的完整路径。</summary>
    public required string DataDirectory { get; init; }

    /// <summary>部署配置文件的完整路径。</summary>
    public string ConfigPath => Path.Combine(DataDirectory, CONFIG_FILE_NAME);

    /// <summary>按环境变量与启动目录确定部署路径，并在数据目录不存在时创建。</summary>
    public static DeploymentPaths Resolve()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string? configured   = Environment.GetEnvironmentVariable(DATA_DIRECTORY_VARIABLE);
        string dataDirectory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(baseDirectory, DEFAULT_DATA_DIRECTORY_NAME)
            : Path.GetFullPath(configured, baseDirectory);
        Directory.CreateDirectory(dataDirectory);
        return new DeploymentPaths
        {
            BaseDirectory = baseDirectory,
            DataDirectory = dataDirectory
        };
    }
}
