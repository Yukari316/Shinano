using Microsoft.Extensions.Logging.Abstractions;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Hso;
using Shinano.Plugin.Hso.Info;
using Shinano.Plugin.Hso.Persistence;
using Shinano.Plugin.ImageSources;
using Shinano.Plugin.ImageSources.Gateway;
using Shinano.Tests.Support;

namespace Shinano.Tests.Plugins;

/// <summary>
/// 调用真实图源服务补全作品信息（每次运行消耗一次 SauceNAO 额度）。只在设置 <c>SHINANO_TEST_GATEWAY_TOKEN</c> 时运行，
/// 用于修改作品信息查询相关功能时验证，平时跳过。
/// </summary>
public sealed class HsoLiveLookupTests
{
    // 样例图片的每个字节与该值异或后保存，避免以图片形式出现在仓库中。
    private const byte FIXTURE_KEY = 0x5A;

    private const string FIXTURES = "Plugins/HsoFixtures";

    /// <summary>
    /// 样例图片在 SauceNAO 中相似度最高的是图站帖子（未标注 Pixiv 来源），同一作品的 Pixiv 结果相似度略低；补全应采用 Pixiv 的作品信息。
    /// </summary>
    [Fact]
    public async Task SauceNaoLookup_PrefersPixivWork()
    {
        TestEnvironment.RequireGateway();
        byte[] image = Fixture("booru-pixiv-source.bin");
        TestContext context = new() { HsoConfig = new HsoConfig() };
        using YukariGateway gateway = new(
            new Settings
            {
                BaseUrl = TestEnvironment.GatewayUrl ?? context.HsoConfig.GatewayUrl,
                Token   = TestEnvironment.GatewayToken
            },
            NullLogger<YukariGateway>.Instance);
        HsoInfoResolver resolver = new(context, gateway, new ImageResolver(gateway), NullLogger<HsoInfoResolver>.Instance);

        HsoLookup lookup = await resolver.LookupAsync(new HsoImageRecord { MediaType = "image/jpeg" }, image, CancellationToken.None);

        Assert.Equal(HsoLookupStatus.Found, lookup.Status);
        HsoWorkInfo info = lookup.Info ?? throw new InvalidOperationException("没有作品信息");
        Assert.Equal(("Pixiv", ImageSite.Pixiv), (info.SourceName, info.Work?.Site));
        Assert.NotNull(info.Pid);
        Assert.NotNull(info.AuthorId);
        Assert.NotEmpty(info.Tags);
    }

    private static byte[] Fixture(string name) =>
        [.. File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, FIXTURES, name)).Select(value => (byte)(value ^ FIXTURE_KEY))];

    private sealed class Settings : IGatewaySettings
    {
        public required string BaseUrl { get; init; }

        public string? Token { get; init; }
    }

    // 补全只用到插件配置（SauceNAO 阈值）。
    private sealed class TestContext : IPluginInitializationContext
    {
        public required HsoConfig HsoConfig { get; init; }

        public string PluginId => "hso";

        public IPluginEvents Events => throw new NotSupportedException();

        public IPluginScheduler Scheduler => throw new NotSupportedException();

        public IPluginDatabase Database => throw new NotSupportedException();

        public IPluginMedia Media => throw new NotSupportedException();

        public IPluginBuckets Buckets => throw new NotSupportedException();

        public IBotAccessor Bot => throw new NotSupportedException();

        public IPluginAssets Assets => throw new NotSupportedException();

        public IPluginPanel Panel => throw new NotSupportedException();

        public IPluginFonts Fonts => throw new NotSupportedException();

        public IPluginImages Images => throw new NotSupportedException();

        public IPluginBrowser Browser => throw new NotSupportedException();

        public IPluginTools Tools => throw new NotSupportedException();

        public string DataDirectory => throw new NotSupportedException();

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public void OnEnabledChanged(Action<bool> handler) => throw new NotSupportedException();

        public IPluginNotices Notices => throw new NotSupportedException();

        public IPluginConfig<T> Config<T>() where T : class, new() =>
            HsoConfig is T config ? new FixedConfig<T> { Value = config } : throw new NotSupportedException();

        public IPluginSecrets<T> Secrets<T>() where T : class, new() => throw new NotSupportedException();
    }

    private sealed class FixedConfig<T> : IPluginConfig<T> where T : class
    {
        public required T Value { get; init; }
    }
}
