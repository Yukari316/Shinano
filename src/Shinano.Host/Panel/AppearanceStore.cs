using System.Text.Json;
using Shinano.Host.Configuration;
using Shinano.Infrastructure.Media;

namespace Shinano.Host.Panel;

/// <summary>
/// 面板外观设置与背景图，保存在部署数据目录的 <c>panel</c> 下，不依赖数据库与 RustFS，故障模式下同样可用。
/// </summary>
public sealed class AppearanceStore
{
    /// <summary>不透明度上限。</summary>
    public const int MAX_OPACITY = 100;

    /// <summary>背景图大小上限，单位字节。</summary>
    public const long MAX_BACKGROUND_BYTES = 10L * 1024 * 1024;

    private const string DIRECTORY_NAME  = "panel";
    private const string SETTINGS_FILE   = "appearance.json";
    private const string BACKGROUND_NAME = "background";

    private static readonly Dictionary<string, string> ImageExtensions = new()
    {
        ["image/png"]  = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/gif"]  = ".gif",
        ["image/webp"] = ".webp"
    };

    private readonly string _directory;
    private readonly Lock   _lock = new();

    /// <summary>创建存储。</summary>
    public AppearanceStore(DeploymentPaths paths)
    {
        _directory = Path.Combine(paths.DataDirectory, DIRECTORY_NAME);
    }

    /// <summary>读取设置；文件不存在或无法解析时返回默认值。</summary>
    public AppearanceSettings Read()
    {
        lock (_lock)
        {
            string path = Path.Combine(_directory, SETTINGS_FILE);
            if (!File.Exists(path)) return new AppearanceSettings();

            try
            {
                return JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path)) ?? new AppearanceSettings();
            }
            catch (JsonException)
            {
                return new AppearanceSettings();
            }
        }
    }

    /// <summary>修改不透明度与模糊，不透明度超出 0–100 时截断。</summary>
    public void Apply(AppearanceUpdate update) =>
        Update(settings =>
        {
            settings.PanelOpacity = Math.Clamp(update.PanelOpacity, 0, MAX_OPACITY);
            settings.CardOpacity  = Math.Clamp(update.CardOpacity, 0, MAX_OPACITY);
            settings.VeilOpacity  = Math.Clamp(update.VeilOpacity, 0, MAX_OPACITY);
            settings.Blur         = update.Blur;
        });

    /// <summary>保存背景图，替换已有背景图。</summary>
    /// <returns>错误原因；成功时为 null。</returns>
    public async Task<string?> SaveBackgroundAsync(Stream content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        await using BufferedMedia media = await BufferedMedia.CreateAsync(content, MAX_BACKGROUND_BYTES, cancellationToken);
        string mediaType = await MediaTypeSniffer.DetectAsync(media.FilePath, cancellationToken);
        if (!ImageExtensions.TryGetValue(mediaType, out string? extension)) return "只支持 PNG、JPEG、GIF、WebP 图片";

        string fileName = BACKGROUND_NAME + extension;
        File.Copy(media.FilePath, Path.Combine(_directory, fileName), overwrite: true);
        Update(settings =>
        {
            if (settings.BackgroundFile is { } previous && previous != fileName) File.Delete(Path.Combine(_directory, previous));
            settings.BackgroundFile = fileName;
        });
        return null;
    }

    /// <summary>删除背景图。</summary>
    public void DeleteBackground() =>
        Update(settings =>
        {
            if (settings.BackgroundFile is { } previous) File.Delete(Path.Combine(_directory, previous));
            settings.BackgroundFile = null;
        });

    /// <summary>返回背景图的完整路径；没有背景图时返回 null。</summary>
    public string? BackgroundPath() =>
        Read().BackgroundFile is { } file && File.Exists(Path.Combine(_directory, file)) ? Path.Combine(_directory, file) : null;

    private void Update(Action<AppearanceSettings> change)
    {
        lock (_lock)
        {
            AppearanceSettings settings = Read();
            change(settings);
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, SETTINGS_FILE), JsonSerializer.Serialize(settings));
        }
    }
}
