using Shinano.Host.Plugins.Staging;

namespace Shinano.Tests.Host.Restart;

public sealed class PluginPackageStagingTests : IDisposable
{
    private readonly PackageFixture       _fixture = new();
    private readonly PluginPackageStaging _staging;

    public PluginPackageStagingTests() => _staging = _fixture.CreateStaging();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task NewPackage_IsStagedAsInstall()
    {
        await using Stream zip = PackageFixture.Zip(PackageFixture.Package("sample-a", "1.0.0"));

        PendingPluginChange change = await _staging.StageAsync(zip, TestContext.Current.CancellationToken);

        Assert.Equal(PluginChangeKind.Install, change.Change.Kind);
        Assert.Null(change.Change.FromVersion);
        Assert.Equal("1.0.0", change.Change.ToVersion);
        Assert.True(File.Exists(Path.Combine(_staging.PackageDirectory("sample-a"), "assets", "a.txt")));
        Assert.Single(_staging.Pending());
    }

    [Fact]
    public async Task InstalledId_IsStagedAsUpgrade_AllowingDowngrade()
    {
        _fixture.Install("SampleA", "sample-a", "2.0.0");
        await using Stream zip = PackageFixture.Zip(PackageFixture.Package("sample-a", "1.0.0"));

        PendingPluginChange change = await _staging.StageAsync(zip, TestContext.Current.CancellationToken);

        Assert.Equal(PluginChangeKind.Upgrade, change.Change.Kind);
        Assert.Equal("2.0.0", change.Change.FromVersion);
        Assert.Equal("1.0.0", change.Change.ToVersion);
    }

    [Fact]
    public async Task Reupload_ReplacesEarlierStaging()
    {
        await using (Stream first = PackageFixture.Zip(PackageFixture.Package("sample-a", "1.0.0")))
            await _staging.StageAsync(first, TestContext.Current.CancellationToken);
        await using (Stream second = PackageFixture.Zip(PackageFixture.Package("sample-a", "1.0.1")))
            await _staging.StageAsync(second, TestContext.Current.CancellationToken);

        PendingPluginChange change = Assert.Single(_staging.Pending());
        Assert.Equal("1.0.1", change.Change.ToVersion);
        Assert.Equal("1.0.1", File.ReadAllText(Path.Combine(_staging.PackageDirectory("sample-a"), "Sample.dll")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("assets/../../escape.txt")]
    public async Task EntryEscapingPackage_IsRejected(string entryName)
    {
        Dictionary<string, string> entries = PackageFixture.Package("sample-a", "1.0.0");
        entries[entryName] = "x";
        await using Stream zip = PackageFixture.Zip(entries);

        await Assert.ThrowsAsync<InvalidDataException>(() => _staging.StageAsync(zip, TestContext.Current.CancellationToken));
        Assert.Empty(_staging.Pending());
        Assert.False(File.Exists(Path.Combine(_staging.Root, "escape.txt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_staging.Root, ".incoming-*"));
    }

    [Theory]
    [InlineData("plugin.json")]
    [InlineData("Sample.dll")]
    public async Task MissingManifestOrEntryAssembly_IsRejected(string missing)
    {
        Dictionary<string, string> entries = PackageFixture.Package("sample-a", "1.0.0");
        entries.Remove(missing);
        await using Stream zip = PackageFixture.Zip(entries);

        await Assert.ThrowsAsync<InvalidDataException>(() => _staging.StageAsync(zip, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BucketPurposeUsedByAnotherPlugin_IsRejected_UnlessThatPluginIsBeingRemoved()
    {
        _fixture.Install("Gallery", "gallery", "1.0.0", "images");

        await using (Stream conflicting = PackageFixture.Zip(PackageFixture.Package("sample-a", "1.0.0", "images")))
            await Assert.ThrowsAsync<InvalidDataException>(() => _staging.StageAsync(conflicting, TestContext.Current.CancellationToken));

        Assert.NotNull(_staging.StageRemoval("gallery", purgeData: false));
        await using Stream allowed = PackageFixture.Zip(PackageFixture.Package("sample-a", "1.0.0", "images"));
        await _staging.StageAsync(allowed, TestContext.Current.CancellationToken);
        Assert.Equal(2, _staging.Pending().Count);
    }

    [Fact]
    public async Task Undo_RemovesChangeAndStagedPackage()
    {
        await using Stream zip = PackageFixture.Zip(PackageFixture.Package("sample-a", "1.0.0"));
        await _staging.StageAsync(zip, TestContext.Current.CancellationToken);

        Assert.True(_staging.Undo("sample-a"));

        Assert.Empty(_staging.Pending());
        Assert.False(Directory.Exists(_staging.PackageDirectory("sample-a")));
    }

    [Fact]
    public void Frozen_RejectsChanges()
    {
        _fixture.Install("SampleA", "sample-a", "1.0.0");
        _staging.Freeze();

        Assert.Throws<InvalidOperationException>(() => _staging.StageRemoval("sample-a", purgeData: false));
    }
}
