using Shinano.Framework.Plugins.Capabilities;

namespace Shinano.Plugin.OnlineCheck;

/// <summary>
/// 回复用的固定图片，插件初始化时读入内存并编码为 <c>base64://</c> URI：LLBot 与 Bot 不在同一台机器，不能使用本地文件路径。
/// </summary>
/// <remarks>指令实例在插件初始化之前构造，因此由插件在初始化时加载；插件启用前指令不会被调用。</remarks>
public sealed class OnlineCheckAssets
{
    private const string POKE_IMAGE   = "poke.jpg";
    private const string ONLINE_IMAGE = "online.gif";

    private string? _pokeImageUri;
    private string? _onlineImageUri;

    /// <summary>戳一戳回复的图片。</summary>
    /// <exception cref="InvalidOperationException">尚未加载。</exception>
    public string PokeImageUri => _pokeImageUri ?? throw new InvalidOperationException("素材尚未加载");

    /// <summary>私聊"在"回复的图片。</summary>
    /// <exception cref="InvalidOperationException">尚未加载。</exception>
    public string OnlineImageUri => _onlineImageUri ?? throw new InvalidOperationException("素材尚未加载");

    /// <summary>读取插件包 <c>assets/</c> 中的图片。</summary>
    public async Task LoadAsync(IPluginAssets assets, CancellationToken cancellationToken)
    {
        _pokeImageUri   = ToBase64Uri(await assets.ReadAllBytesAsync(POKE_IMAGE, cancellationToken));
        _onlineImageUri = ToBase64Uri(await assets.ReadAllBytesAsync(ONLINE_IMAGE, cancellationToken));
    }

    private static string ToBase64Uri(byte[] content) => "base64://" + Convert.ToBase64String(content);
}
