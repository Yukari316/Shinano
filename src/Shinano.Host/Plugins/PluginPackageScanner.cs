namespace Shinano.Host.Plugins;

/// <summary>读取插件目录下已安装的插件包清单，不加载程序集。</summary>
public static class PluginPackageScanner
{
    /// <summary>按目录名顺序列出含 <c>plugin.json</c> 的子目录；插件目录不存在时为空。</summary>
    public static IReadOnlyList<PluginPackageDirectory> Scan(string root)
    {
        if (!Directory.Exists(root)) return [];

        List<PluginPackageDirectory> packages = [];
        foreach (string directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            string manifestPath = Path.Combine(directory, PluginManifest.FILE_NAME);
            if (!File.Exists(manifestPath)) continue;

            try
            {
                packages.Add(
                    new PluginPackageDirectory
                    {
                        Directory = directory,
                        Manifest  = PluginManifest.Read(manifestPath)
                    });
            }
            catch (InvalidDataException exception)
            {
                packages.Add(
                    new PluginPackageDirectory
                    {
                        Directory = directory,
                        Error     = exception.Message
                    });
            }
        }

        return packages;
    }
}
