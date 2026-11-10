using System.Text.Json;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Models;

namespace Shinano.Tests.Plugins;

/// <summary>插件清单的数据表声明校验与可能的残留表判定。</summary>
public sealed class PluginDataPurgeTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("shinano-manifest-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Tables_WithPluginPrefix_AreAccepted()
    {
        PluginManifest manifest = Read("image-search", new { global = new[] { "image_search_cache" }, account = new[] { "image_search_usage" } });

        Assert.Equal(["image_search_cache"], manifest.Tables.Global);
        Assert.Equal(["image_search_usage"], manifest.Tables.Account);
        Assert.Empty(Read("image-search", null).Tables.Global);
    }

    [Theory]
    [InlineData("search_cache")]
    [InlineData("image-search_cache")]
    [InlineData("image_search_")]
    [InlineData("Image_search_cache")]
    [InlineData("image_search_a_very_long_table_name_that_goes_beyond_the_postgres_limit")]
    public void Tables_OutsidePluginPrefixOrInvalid_AreRejected(string table) =>
        Assert.Throws<InvalidDataException>(() => Read("image-search", new { global = new[] { table }, account = Array.Empty<string>() }));

    [Fact]
    public void Tables_DeclaredTwice_AreRejected() =>
        Assert.Throws<InvalidDataException>(() => Read("qa", new { global = new[] { "qa_entry" }, account = new[] { "qa_entry" } }));

    [Theory]
    [InlineData("image_search_old", true)]
    [InlineData("IMAGE_SEARCH_Old", true)]
    [InlineData("image-search_old", true)]
    [InlineData("image-search-old", true)]
    [InlineData("image_search-old", true)]
    [InlineData("imagesearch_old", false)]
    [InlineData("image_searcher", false)]
    [InlineData("image_search_declared_by_other", false)]
    public void PossibleLeftover_MatchesPrefixesIgnoringCaseAndSkipsOtherPluginsTables(string table, bool expected) =>
        Assert.Equal(
            expected,
            PluginDataPurger.IsPossibleLeftover(table, PluginDataPurger.LeftoverPrefixes("image-search"), new HashSet<string> { "image_search_declared_by_other" }));

    private PluginManifest Read(string id, object? tables)
    {
        string path = Path.Combine(_directory, PluginManifest.FILE_NAME);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                tables is null
                    ? new Dictionary<string, object> { ["id"] = id, ["entryAssembly"] = "Sample.dll" }
                    : new Dictionary<string, object> { ["id"] = id, ["entryAssembly"] = "Sample.dll", ["tables"] = tables }));
        return PluginManifest.Read(path);
    }
}
