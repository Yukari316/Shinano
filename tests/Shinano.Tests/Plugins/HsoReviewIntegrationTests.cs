using Microsoft.Extensions.Logging.Abstractions;
using Shinano.Framework.Plugins;
using Shinano.Host.Plugins.Capabilities;
using Shinano.Infrastructure.Hosting;
using Shinano.Plugin.Hso.Gallery;
using Shinano.Plugin.Hso.Info;
using Shinano.Plugin.Hso.Panel;
using Shinano.Plugin.Hso.Persistence;
using Shinano.Plugin.ImageSources;
using Shinano.Tests.Support;
using SkiaSharp;
using SqlSugar;

namespace Shinano.Tests.Plugins;

/// <summary>
/// Hso 作品信息审核：对真实的测试 schema 与 bucket 核对三种处理方式对图片记录、图库对象与暂存原图的改动。
/// 外部服务未配置时跳过；bucket 在每个测试结束后删除，schema 由 <see cref="InfrastructureFixture" /> 删除。
/// </summary>
[Collection(InfrastructureCollection.NAME)]
public sealed class HsoReviewIntegrationTests : IAsyncLifetime
{
    private const long DEPLOYMENT_LIMIT = 16L * 1024 * 1024;
    private const int  SIZE             = 24;
    private const int  BYTE_BITS        = 8;
    private const byte COLOR_BLUE       = 128;

    private static int ColorCounter;

    private readonly InfrastructureFixture _fixture;
    private HsoGallery?                    _gallery;
    private HsoReviews?                    _reviews;

    public HsoReviewIntegrationTests(InfrastructureFixture fixture) => _fixture = fixture;

    private HsoGallery Gallery => _gallery ?? throw new InvalidOperationException("测试未初始化");

    private HsoReviews Reviews => _reviews ?? throw new InvalidOperationException("测试未初始化");

    public async ValueTask InitializeAsync()
    {
        if (!TestEnvironment.IsConfigured) return;

        HostInfrastructure infrastructure = _fixture.Ready;
        TestContext context = new()
        {
            Database = new GlobalDatabase { Global = infrastructure.Database.Global },
            Buckets  = new PluginBuckets([HsoGallery.BUCKET_PURPOSE], _fixture.BucketPrefix, infrastructure.Storage, DEPLOYMENT_LIMIT)
        };
        _gallery = new HsoGallery(context, TimeProvider.System, NullLogger<HsoGallery>.Instance);
        _reviews = new HsoReviews(context, _gallery, TimeProvider.System, NullLogger<HsoReviews>.Instance);
        await _gallery.InitializeAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_gallery is not null) await _gallery.Bucket.DeleteAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Replace_SwapsObjectConfirmsInfoAndRemovesCandidate()
    {
        TestEnvironment.RequireConfigured();
        HsoImageRecord record = await ImportAsync();
        await Gallery.MarkBlockedAsync(record);
        byte[] candidate = Png();
        await EnqueueAsync(record, candidate);

        HsoReviewView? view = await Reviews.DetailAsync(record.Id, CancellationToken.None);
        Assert.NotNull(view);
        Assert.Equal((SIZE, SIZE), (view.Current.Width, view.Current.Height));
        Assert.NotEqual(view.Current.Sha256, view.Candidate?.Sha256);
        Assert.Equal(HsoInfoState.Review, (HsoInfoState)(await Find(record.Id)).InfoState);
        string candidateKey = (await Reviews.FindAsync(record.Id))?.ObjectKey ?? throw new InvalidOperationException("没有暂存原图");

        Assert.Equal(HsoReviewOutcome.Done, (await Reviews.ReplaceAsync(record.Id, CancellationToken.None)).Outcome);

        HsoImageRecord replaced = await Find(record.Id);
        Assert.Equal(view.Candidate?.Sha256, replaced.Sha256);
        Assert.Equal((HsoInfoState.Found, false, "夏の終わり"), ((HsoInfoState)replaced.InfoState, replaced.Blocked, replaced.Title));
        Assert.Equal(candidate, await Gallery.ReadAsync(replaced, CancellationToken.None));
        Assert.Null(await Gallery.Bucket.OpenReadAsync(record.ObjectKey, CancellationToken.None));
        Assert.Null(await Gallery.Bucket.OpenReadAsync(candidateKey, CancellationToken.None));
        Assert.Null(await Reviews.FindAsync(record.Id));
    }

    // 每日补全曾总取最早导入的待补全图片，一张持续查询失败的图会让每天的补全都卡在它上面。
    [Fact]
    public async Task PendingIds_PutTriedImagesAfterUntriedOnes()
    {
        TestEnvironment.RequireConfigured();
        HsoImageRecord older = await ImportAsync();
        HsoImageRecord newer = await ImportAsync();
        Assert.Equal(HsoInfoState.Pending, (HsoInfoState)older.InfoState);

        await Gallery.MarkInfoTriedAsync(older.Id, DateTimeOffset.UtcNow);
        List<long> pending = await Gallery.PendingIdsAsync(int.MaxValue);

        Assert.True(pending.IndexOf(newer.Id) < pending.IndexOf(older.Id));
    }

    [Fact]
    public async Task Replace_RefusesWhenCandidateDuplicatesAnotherImage()
    {
        TestEnvironment.RequireConfigured();
        byte[] existing = Png();
        HsoImageRecord other = await ImportAsync(existing);
        HsoImageRecord record = await ImportAsync();
        await EnqueueAsync(record, existing);

        HsoReviewResult result = await Reviews.ReplaceAsync(record.Id, CancellationToken.None);

        Assert.Equal((HsoReviewOutcome.Duplicate, other.Id), (result.Outcome, result.DuplicateId));
        Assert.NotNull(await Reviews.FindAsync(record.Id));
    }

    [Fact]
    public async Task Accept_KeepsImageAndConfirmsInfo()
    {
        TestEnvironment.RequireConfigured();
        HsoImageRecord record = await ImportAsync();
        await EnqueueAsync(record, Png());

        Assert.Equal(HsoReviewOutcome.Done, (await Reviews.AcceptAsync(record.Id, CancellationToken.None)).Outcome);

        HsoImageRecord accepted = await Find(record.Id);
        Assert.Equal((HsoInfoState.Found, record.Sha256, 127307417L), ((HsoInfoState)accepted.InfoState, accepted.Sha256, accepted.Pid));
        Assert.Null(await Reviews.FindAsync(record.Id));
        Assert.Equal(HsoReviewOutcome.NotFound, (await Reviews.AcceptAsync(record.Id, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Reject_DiscardsSauceNaoInfoOrSavesManualInfo()
    {
        TestEnvironment.RequireConfigured();
        HsoImageRecord empty = await ImportAsync();
        await EnqueueAsync(empty, Png(), nsfw: true);
        HsoImageRecord manual = await ImportAsync();
        await EnqueueAsync(manual, null);

        await Reviews.RejectAsync(empty.Id, new HsoManualInfo(), CancellationToken.None);
        await Reviews.RejectAsync(manual.Id, new HsoManualInfo { Title = " 手动标题 ", Tags = ["原创", " ", "原创"] }, CancellationToken.None);

        // SauceNAO 查到的 pid 一并作废，NSFW 恢复默认，记为查无信息。
        HsoImageRecord rejected = await Find(empty.Id);
        Assert.Equal((HsoInfoState.NotFound, (long?)null, (string?)null, false), ((HsoInfoState)rejected.InfoState, rejected.Pid, rejected.Title, rejected.Nsfw));
        HsoImageRecord completed = await Find(manual.Id);
        Assert.Equal((HsoInfoState.Found, "手动标题", (long?)null), ((HsoInfoState)completed.InfoState, completed.Title, completed.Pid));
        Assert.Equal(["原创"], completed.TagList);
        // 图库表在本次运行的各个测试间共用，只核对本测试的两张图已出队。
        List<long> queued = await Reviews.IdsAsync();
        Assert.DoesNotContain(empty.Id, queued);
        Assert.DoesNotContain(manual.Id, queued);
    }

    [Fact]
    public async Task DeletingImageRemovesItsReview()
    {
        TestEnvironment.RequireConfigured();
        HsoImageRecord record = await ImportAsync();
        await EnqueueAsync(record, Png());
        string candidateKey = (await Reviews.FindAsync(record.Id))?.ObjectKey ?? throw new InvalidOperationException("没有暂存原图");

        Assert.True(await Gallery.DeleteAsync(record.Id, CancellationToken.None));

        Assert.Null(await Reviews.FindAsync(record.Id));
        Assert.Null(await Gallery.Bucket.OpenReadAsync(candidateKey, CancellationToken.None));
    }

    private async Task<HsoImageRecord> ImportAsync(byte[]? content = null)
    {
        HsoImportResult result = await Gallery.ImportAsync(content ?? Png(), "upload.png", HsoImageOrigin.Upload, null, null, CancellationToken.None);
        Assert.Equal(HsoImportOutcome.Imported, result.Outcome);
        return await Find(result.Id ?? throw new InvalidOperationException("导入没有返回 ID"));
    }

    // 模拟 SauceNAO 匹配到 Pixiv 作品：图片原本没有 pid，查到的信息带 pid。
    private async Task EnqueueAsync(HsoImageRecord record, byte[]? candidate, bool nsfw = false)
    {
        HsoWorkInfo info = new()
        {
            Method     = HsoLookupMethod.SauceNao,
            Similarity = 92.4,
            Work = new SourceRef
            {
                Site = ImageSite.Pixiv,
                Id   = "127307417",
                Page = 0
            },
            SourceName = "Pixiv",
            Pid        = 127307417,
            Page       = 0,
            Title      = "夏の終わり",
            Author     = "再音さく",
            Tags       = ["オリジナル"],
            Nsfw       = nsfw
        };
        (byte[], HsoImageFormat)? image = candidate is null ? null : (candidate, HsoImageFiles.Detect(candidate) ?? throw new InvalidOperationException("不是图片"));
        await Reviews.EnqueueAsync(record, info, image, candidate is null ? "图源服务没有返回原图" : null, CancellationToken.None);
    }

    private async Task<HsoImageRecord> Find(long id) => await Gallery.FindAsync(id) ?? throw new InvalidOperationException($"图片 {id} 不存在");

    // 每次一种新颜色，保证内容（SHA256）不同；图库表在本次运行的各个测试间共用，计数为全局。
    private static byte[] Png()
    {
        int color = Interlocked.Increment(ref ColorCounter);
        using SKBitmap bitmap = new(SIZE, SIZE);
        bitmap.Erase(new SKColor((byte)color, (byte)(color >> BYTE_BITS), COLOR_BLUE));
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class GlobalDatabase : IPluginDatabase
    {
        public required ISqlSugarClient Global { get; init; }

        public ISqlSugarClient Account => throw new NotSupportedException();

        public long SelfId => throw new NotSupportedException();

        public Task MigrateGlobalAsync(IReadOnlyList<PluginMigration> migrations, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MigrateAccountAsync(IReadOnlyList<PluginMigration> migrations, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    // Hso 的图库与审核只用到数据库与插件 bucket。
    private sealed class TestContext : IPluginInitializationContext
    {
        public required IPluginDatabase Database { get; init; }

        public required IPluginBuckets Buckets { get; init; }

        public string PluginId => "hso";

        public IPluginEvents Events => throw new NotSupportedException();

        public IPluginScheduler Scheduler => throw new NotSupportedException();

        public IPluginMedia Media => throw new NotSupportedException();

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

        public IPluginConfig<T> Config<T>() where T : class, new() => throw new NotSupportedException();

        public IPluginSecrets<T> Secrets<T>() where T : class, new() => throw new NotSupportedException();
    }
}
