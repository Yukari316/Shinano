namespace Shinano.Framework.Plugins;

/// <summary>插件包 <c>assets/</c> 目录的只读访问。</summary>
public interface IPluginAssets
{
    /// <summary>读取素材文件的全部内容。</summary>
    /// <param name="relativePath">相对 <c>assets/</c> 的路径，使用 <c>/</c> 分隔。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="ArgumentException">路径指向 <c>assets/</c> 之外。</exception>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    Task<byte[]> ReadAllBytesAsync(string relativePath, CancellationToken cancellationToken);
}
