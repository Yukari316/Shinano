using Shinano.Framework.Plugins;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>插件包 <c>assets/</c> 目录的只读访问，拒绝指向目录之外的路径。</summary>
internal sealed class PluginAssets : IPluginAssets
{
    /// <summary>素材目录名。</summary>
    public const string DIRECTORY_NAME = "assets";

    private readonly string _root;

    /// <summary>创建访问入口。</summary>
    /// <param name="pluginDirectory">插件包目录。</param>
    public PluginAssets(string pluginDirectory)
    {
        _root = Path.GetFullPath(Path.Combine(pluginDirectory, DIRECTORY_NAME)) + Path.DirectorySeparatorChar;
    }

    /// <inheritdoc />
    public Task<byte[]> ReadAllBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        string path = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!path.StartsWith(_root, StringComparison.Ordinal))
            throw new ArgumentException($"素材路径越界：{relativePath}", nameof(relativePath));
        return File.ReadAllBytesAsync(path, cancellationToken);
    }
}
