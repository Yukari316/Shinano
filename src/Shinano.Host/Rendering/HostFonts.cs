using Shinano.Framework.Plugins;
using Shinano.Framework.Rendering;
using SkiaSharp;

namespace Shinano.Host.Rendering;

/// <summary>
/// 宿主的公共字体，随宿主交付于程序目录的 <c>Resource/Fonts</c>：Noto Sans SC 常规与粗体、Saira Expanded（终末地宽体）中粗与粗体、
/// JetBrains Mono（均为 OFL）、Twemoji Mozilla（图形 CC-BY 4.0）。使用静态字重文件：SkiaSharp 直接加载可变字体时只取默认字重。
/// 选用 COLRv0 格式的 Twemoji：当前 Noto Color Emoji（COLRv1 + SVG）在 Windows 上经 SkiaSharp 绘制为空白。
/// 字体缺失时不阻止宿主启动，访问时抛出异常，由调用方改用文字。
/// </summary>
public sealed class HostFonts : IPluginFonts, IDisposable
{
    /// <summary>资源目录名，位于程序目录下，随宿主输出。</summary>
    public const string RESOURCE_DIRECTORY = "Resource";

    private const string FONTS_DIRECTORY = "Fonts";
    private const string REGULAR_FILE    = "NotoSansSC-Regular.otf";
    private const string BOLD_FILE       = "NotoSansSC-Bold.otf";
    private const string LATIN_FILE      = "SairaExpanded-Medium.ttf";
    private const string LATIN_BOLD_FILE = "SairaExpanded-Bold.ttf";
    private const string MONO_FILE       = "JetBrainsMono-Regular.ttf";
    private const string EMOJI_FILE      = "TwemojiMozilla.ttf";

    private readonly SKTypeface?  _regular;
    private readonly SKTypeface?  _bold;
    private readonly SKTypeface?  _latin;
    private readonly SKTypeface?  _latinBold;
    private readonly SKTypeface?  _mono;
    private readonly SKTypeface?  _emoji;
    private readonly TextPainter? _painter;

    /// <summary>从程序目录下的资源目录加载字体。</summary>
    /// <param name="baseDirectory">程序目录。</param>
    public HostFonts(string baseDirectory)
    {
        string directory = Path.Combine(baseDirectory, RESOURCE_DIRECTORY, FONTS_DIRECTORY);
        _regular   = Load(Path.Combine(directory, REGULAR_FILE));
        _bold      = Load(Path.Combine(directory, BOLD_FILE));
        _latin     = Load(Path.Combine(directory, LATIN_FILE));
        _latinBold = Load(Path.Combine(directory, LATIN_BOLD_FILE));
        _mono      = Load(Path.Combine(directory, MONO_FILE));
        _emoji     = Load(Path.Combine(directory, EMOJI_FILE));
        string[] missing =
        [
            .. new[]
               {
                   (REGULAR_FILE, _regular), (BOLD_FILE, _bold), (LATIN_FILE, _latin), (LATIN_BOLD_FILE, _latinBold),
                   (MONO_FILE, _mono), (EMOJI_FILE, _emoji)
               }
               .Where(item => item.Item2 is null)
               .Select(item => item.Item1)
        ];
        if (missing.Length > 0) LoadError = $"无法加载 {directory} 中的字体：{string.Join("、", missing)}";
        else if (_regular is not null && _emoji is not null) _painter = new TextPainter([_regular, _emoji]);
    }

    /// <summary>字体加载失败的原因；全部加载成功时为 null。</summary>
    public string? LoadError { get; }

    /// <inheritdoc />
    public SKTypeface Regular => _regular ?? throw Unavailable();

    /// <inheritdoc />
    public SKTypeface Bold => _bold ?? throw Unavailable();

    /// <inheritdoc />
    public SKTypeface Latin => _latin ?? throw Unavailable();

    /// <inheritdoc />
    public SKTypeface LatinBold => _latinBold ?? throw Unavailable();

    /// <inheritdoc />
    public SKTypeface Mono => _mono ?? throw Unavailable();

    /// <inheritdoc />
    public SKTypeface Emoji => _emoji ?? throw Unavailable();

    /// <inheritdoc />
    public TextPainter Painter => _painter ?? throw Unavailable();

    /// <inheritdoc />
    public void Dispose()
    {
        _regular?.Dispose();
        _bold?.Dispose();
        _latin?.Dispose();
        _latinBold?.Dispose();
        _mono?.Dispose();
        _emoji?.Dispose();
    }

    private InvalidOperationException Unavailable() => new(LoadError ?? "宿主字体不可用");

    private static SKTypeface? Load(string path) => File.Exists(path) ? SKTypeface.FromFile(path) : null;
}
