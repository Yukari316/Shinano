namespace Shinano.Plugin.ImageSources;

/// <summary>
/// 「H是不行的」占位图：搜图结果为 R-18 时代替原图或预览发送。图片作为资源嵌入本程序集，引用本库的插件包不另带素材。
/// </summary>
public static class AdultPlaceholder
{
    private const string RESOURCE_NAME = "Shinano.Plugin.ImageSources.adult.jpg";
    private const string BASE64_PREFIX = "base64://";

    private static readonly Lazy<string> LazyImageUri = new(Load);

    /// <summary><c>base64://</c> 形式的图片，可直接作为图片消息段发送；首次访问时读取资源，之后复用。</summary>
    public static string ImageUri => LazyImageUri.Value;

    // 资源由工程文件嵌入，缺失只可能是构建错误。
    private static string Load()
    {
        using Stream stream = typeof(AdultPlaceholder).Assembly.GetManifestResourceStream(RESOURCE_NAME)
                              ?? throw new InvalidOperationException($"程序集中缺少嵌入资源 {RESOURCE_NAME}");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return BASE64_PREFIX + Convert.ToBase64String(buffer.ToArray());
    }
}
