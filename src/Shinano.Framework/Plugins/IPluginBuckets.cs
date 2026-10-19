namespace Shinano.Framework.Plugins;

/// <summary>插件独立 bucket 的入口。只能访问 <c>plugin.json</c> 中声明的用途。</summary>
public interface IPluginBuckets
{
    /// <summary>返回用途为 <paramref name="purpose" /> 的 bucket。</summary>
    /// <exception cref="ArgumentException">用途未在 <c>plugin.json</c> 中声明。</exception>
    IPluginBucket Get(string purpose);
}
