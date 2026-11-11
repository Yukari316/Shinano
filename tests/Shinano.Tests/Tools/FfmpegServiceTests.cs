using Microsoft.Extensions.Logging.Abstractions;
using Shinano.Host.Tools;
using Shinano.Host.Tools.Models;

namespace Shinano.Tests.Tools;

/// <summary>ffmpeg 服务的校验文件解析与指定目录模式的缺失检查；真实下载由开发部署实测。</summary>
public sealed class FfmpegServiceTests
{
    private const string ASSET = "ffmpeg-n8.1-latest-win64-gpl-8.1.zip";

    private const string CHECKSUMS = """
        c30ff1b1ba882f585e50cc7be9c3d021061121734fb71ed08eefdcbeeb576676  ffmpeg-n8.1-latest-win64-gpl-8.1.zip
        55176729c559f7c151810256003c298cbcce64788ada16b90ed131ca2de18d91  ffmpeg-n8.1-latest-linux64-gpl-8.1.tar.xz
        """;

    [Fact]
    public void ExpectedHash_PicksLineOfAsset()
    {
        Assert.Equal("c30ff1b1ba882f585e50cc7be9c3d021061121734fb71ed08eefdcbeeb576676", FfmpegService.ExpectedHash(CHECKSUMS, ASSET));
        Assert.Throws<InvalidOperationException>(() => FfmpegService.ExpectedHash(CHECKSUMS, "ffmpeg-n9.0-latest-win64-gpl-9.0.zip"));
    }

    [Fact]
    public async Task DirectoryMode_ReportsEveryMissingProgram()
    {
        string directory = Directory.CreateTempSubdirectory("shinano-ffmpeg-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"), "");
            FfmpegService service = new(
                new FfmpegOptions
                {
                    Mode      = FfmpegMode.Directory,
                    Directory = directory
                },
                directory,
                NullLogger<FfmpegService>.Instance);

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.FfmpegDirectoryAsync(TestContext.Current.CancellationToken));
            Assert.Contains("ffprobe、ffplay", error.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DirectoryMode_WithoutDirectoryFails()
    {
        FfmpegService service = new(
            new FfmpegOptions { Mode = FfmpegMode.Directory },
            Path.GetTempPath(),
            NullLogger<FfmpegService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FfmpegDirectoryAsync(TestContext.Current.CancellationToken));
    }
}
