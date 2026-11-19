using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;

namespace Shinano.Host.Tools;

/// <summary>
/// 宿主 ffmpeg 服务：按配置解析含 ffmpeg、ffprobe、ffplay 的目录并确认三者可运行；自动下载模式下从 FFmpeg-Builds
/// 下载宿主固定版本分支的静态构建，经发布附带的 SHA256 校验后用系统 <c>tar</c> 解压。线程安全，同一时刻只有一次解析。
/// </summary>
public sealed class FfmpegService : IPluginTools
{
    /// <summary>自动下载的目录名，位于部署数据目录下。</summary>
    public const string DIRECTORY_NAME = "ffmpeg";

    // 版本分支随宿主版本升级；已下载构建的资产名与当前不同时重新下载。
    private const string BRANCH             = "8.1";
    private const string RELEASE_URL        = "https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/latest/";
    private const string CHECKSUMS_FILE     = "checksums.sha256";
    private const string ASSET_MARKER       = "asset.txt";
    private const string WORK_DIRECTORY     = ".download";
    private const string EXTRACT_DIRECTORY  = "extracted";
    private const int    VERSION_TIMEOUT_MS = 10_000;
    private const int    EXTRACT_TIMEOUT_MS = 5 * 60_000;
    private const int    ERROR_TAIL_LENGTH  = 300;
    private const double BYTES_PER_MIB      = 1024 * 1024;

    private static readonly string[] Programs = ["ffmpeg", "ffprobe", "ffplay"];

    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

    private readonly FfmpegOptions          _options;
    private readonly string                 _dataDirectory;
    private readonly HttpClient             _http;
    private readonly ILogger<FfmpegService> _logger;
    private readonly SemaphoreSlim          _gate = new(1, 1);
    private volatile string?                _directory;

    /// <summary>创建服务；不访问网络，第一次调用 <see cref="FfmpegDirectoryAsync" /> 时才解析与下载。</summary>
    /// <param name="options">ffmpeg 配置。</param>
    /// <param name="dataDirectory">部署数据目录。</param>
    /// <param name="logger">日志。</param>
    public FfmpegService(FfmpegOptions options, string dataDirectory, ILogger<FfmpegService> logger)
    {
        _options       = options;
        _dataDirectory = dataDirectory;
        _http          = new HttpClient { Timeout = DownloadTimeout };
        _logger        = logger;
    }

    /// <inheritdoc />
    public async Task<string> FfmpegDirectoryAsync(CancellationToken cancellationToken)
    {
        if (_directory is { } cached) return cached;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_directory is { } resolved) return resolved;

            string directory = _options.Mode switch
            {
                FfmpegMode.SystemPath => FindInPath(),
                FfmpegMode.Directory  => ConfiguredDirectory(),
                _                     => await DownloadedAsync(cancellationToken)
            };
            await VerifyAsync(directory, cancellationToken);
            _logger.LogInformation("ffmpeg 可用：{Directory}（{Mode}）", directory, _options.Mode);
            _directory = directory;
            return directory;
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(exception, "ffmpeg 不可用（{Mode}）", _options.Mode);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string ExecutableName(string program) => OperatingSystem.IsWindows() ? program + ".exe" : program;

    private static string AssetName() =>
        OperatingSystem.IsWindows()
            ? $"ffmpeg-n{BRANCH}-latest-win64-gpl-{BRANCH}.zip"
            : $"ffmpeg-n{BRANCH}-latest-linux64-gpl-{BRANCH}.tar.xz";

    private static string[] Missing(string directory) =>
        [.. Programs.Where(program => !File.Exists(Path.Combine(directory, ExecutableName(program))))];

    // 取 PATH 中第一个含 ffmpeg 的目录；ffprobe、ffplay 须在同一目录，因为插件只拿到一个目录。
    private static string FindInPath()
    {
        string[] entries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? directory = entries.FirstOrDefault(entry => File.Exists(Path.Combine(entry, ExecutableName(Programs[0]))));
        return directory is null
            ? throw new InvalidOperationException("系统 PATH 中找不到 ffmpeg")
            : Path.GetFullPath(directory);
    }

    private string ConfiguredDirectory() =>
        string.IsNullOrWhiteSpace(_options.Directory)
            ? throw new InvalidOperationException("ffmpeg 来源为 Directory，但没有配置目录")
            : Path.GetFullPath(_options.Directory, _dataDirectory);

    private static async Task VerifyAsync(string directory, CancellationToken cancellationToken)
    {
        string[] missing = Missing(directory);
        if (missing.Length > 0) throw new InvalidOperationException($"目录 {directory} 中缺少 {string.Join("、", missing)}");

        foreach (string program in Programs)
        {
            await RunAsync(Path.Combine(directory, ExecutableName(program)), ["-version"], VERSION_TIMEOUT_MS, cancellationToken);
        }
    }

    private async Task<string> DownloadedAsync(CancellationToken cancellationToken)
    {
        string target = Path.Combine(_dataDirectory, DIRECTORY_NAME);
        string asset  = AssetName();
        string marker = Path.Combine(target, ASSET_MARKER);
        if (File.Exists(marker)
            && (await File.ReadAllTextAsync(marker, cancellationToken)).Trim() == asset
            && Missing(target).Length == 0)
        {
            return target;
        }

        string work = Path.Combine(target, WORK_DIRECTORY);
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        try
        {
            string archive = Path.Combine(work, asset);
            await DownloadAsync(asset, archive, cancellationToken);

            string extracted = Path.Combine(work, EXTRACT_DIRECTORY);
            Directory.CreateDirectory(extracted);
            await RunAsync("tar", ["-xf", archive, "-C", extracted], EXTRACT_TIMEOUT_MS, cancellationToken);

            string bin = Directory.EnumerateFiles(extracted, ExecutableName(Programs[0]), SearchOption.AllDirectories)
                                  .Select(Path.GetDirectoryName)
                                  .FirstOrDefault()
                         ?? throw new InvalidOperationException($"{asset} 中找不到 ffmpeg 可执行文件");
            string[] missing = Missing(bin);
            if (missing.Length > 0) throw new InvalidOperationException($"{asset} 中缺少 {string.Join("、", missing)}");

            foreach (string program in Programs)
            {
                string name = ExecutableName(program);
                File.Move(Path.Combine(bin, name), Path.Combine(target, name), overwrite: true);
            }

            // 标记最后写入：中途失败时下次重新下载。
            await File.WriteAllTextAsync(marker, asset, cancellationToken);
            _logger.LogInformation("已安装 ffmpeg {Asset} 到 {Directory}", asset, target);
            return target;
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "删除 ffmpeg 下载临时目录失败：{Directory}", work);
            }
        }
    }

    private async Task DownloadAsync(string asset, string archive, CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始下载 ffmpeg {Asset}", asset);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            string expected = ExpectedHash(await _http.GetStringAsync(RELEASE_URL + CHECKSUMS_FILE, cancellationToken), asset);

            using (HttpResponseMessage response = await _http.GetAsync(
                       RELEASE_URL + asset,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using FileStream file = File.Create(archive);
                await response.Content.CopyToAsync(file, cancellationToken);
            }

            string actual;
            await using (FileStream file = File.OpenRead(archive))
            {
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
            }

            if (actual != expected) throw new InvalidOperationException($"{asset} 的 SHA256 不一致（期望 {expected}，实际 {actual}）");

            _logger.LogInformation(
                "已下载 ffmpeg {Asset}（{SizeMiB:0.0} MiB，用时 {Seconds:0.0} 秒）",
                asset,
                new FileInfo(archive).Length / BYTES_PER_MIB,
                stopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException
                                          && !cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"ffmpeg 下载失败：{exception.Message}", exception);
        }
    }

    // 校验文件每行为「<sha256>  <文件名>」。
    internal static string ExpectedHash(string checksums, string asset) =>
        checksums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                 .Where(parts => parts.Length == 2 && parts[1] == asset)
                 .Select(parts => parts[0].ToLowerInvariant())
                 .FirstOrDefault()
        ?? throw new InvalidOperationException($"{CHECKSUMS_FILE} 中没有 {asset}");

    private static async Task RunAsync(string fileName, IReadOnlyList<string> arguments, int timeoutMs, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using Process process = new() { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"无法运行 {fileName}：{exception.Message}", exception);
        }

        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error  = process.StandardError.ReadToEndAsync(cancellationToken);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException($"{Path.GetFileName(fileName)} 运行超过 {timeoutMs / 1000} 秒未结束");
        }

        await output;
        string stderr = (await error).Trim();
        if (process.ExitCode != 0)
        {
            string tail = stderr.Length > ERROR_TAIL_LENGTH ? stderr[^ERROR_TAIL_LENGTH..] : stderr;
            throw new InvalidOperationException($"{Path.GetFileName(fileName)} {string.Join(' ', arguments)} 退出码 {process.ExitCode}：{tail}");
        }
    }
}
