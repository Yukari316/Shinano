using System.Reflection;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Storage;
using Shinano.Plugin.Hso.Persistence;
using Shinano.Tool.Configuration;
using Shinano.Tool.HsoImport;
using SqlSugar;

namespace Shinano.Tests.Tool;

/// <summary><c>hso-import</c> 直接写入 Hso 插件的图库表与 bucket，这里核对工具维护的表结构与命名约定和插件一致。</summary>
public sealed class HsoImportContractTests
{
    private static readonly PropertyInfo[] RecordProperties = typeof(HsoImageRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    [Fact]
    public void Table_MatchesRecordInGlobalSchema()
    {
        Assert.Equal(GalleryStore.TABLE, typeof(HsoImageRecord).GetCustomAttribute<SugarTable>()?.TableName);
        Assert.Equal(GalleryStore.SCHEMA, new SchemaNames().Global);
    }

    [Fact]
    public void InsertedColumns_CoverEveryRequiredColumn()
    {
        Assert.All(RecordProperties, property => Assert.NotNull(property.GetCustomAttribute<SugarColumn>()));
        // IsIgnore 的属性（如标签的列表形式）不是表列。
        SugarColumn[] columns = [.. RecordProperties.Select(property => property.GetCustomAttribute<SugarColumn>()).OfType<SugarColumn>().Where(column => !column.IsIgnore)];
        string[] names = [.. columns.Select(column => column.ColumnName)];

        Assert.All(GalleryStore.InsertedColumns, column => Assert.Contains(column, names));
        // 插件新增的非空列必须同步到工具，否则工具的插入会失败。
        Assert.All(
            columns.Where(column => column is { IsNullable: false, IsIdentity: false }),
            column => Assert.Contains(column.ColumnName, GalleryStore.InsertedColumns));
        Assert.Equal(GalleryStore.FILE_NAME_MAX_LENGTH, Column(nameof(HsoImageRecord.FileName)).Length);
    }

    [Fact]
    public void Sha256Column_HasUniqueIndexForOnConflict()
    {
        Assert.Equal(GalleryStore.SHA256_COLUMN, Column(nameof(HsoImageRecord.Sha256)).ColumnName);
        Assert.Contains(
            typeof(HsoImageRecord).GetCustomAttributes<SugarIndexAttribute>(),
            index => index.IsUnique && index.IndexFields.Keys.SequenceEqual([nameof(HsoImageRecord.Sha256)]));
    }

    [Fact]
    public void ParameterTypes_MatchRecordProperties()
    {
        Assert.Equal(typeof(long), Property(nameof(HsoImageRecord.Size)).PropertyType);
        Assert.Equal(typeof(long?), Property(nameof(HsoImageRecord.Pid)).PropertyType);
        Assert.Equal(typeof(int?), Property(nameof(HsoImageRecord.Page)).PropertyType);
        Assert.Equal(typeof(int), Property(nameof(HsoImageRecord.InfoState)).PropertyType);
        Assert.Equal(typeof(int), Property(nameof(HsoImageRecord.Origin)).PropertyType);
        Assert.Equal(typeof(DateTimeOffset), Property(nameof(HsoImageRecord.ImportedAt)).PropertyType);
    }

    [Fact]
    public void Conventions_MatchPlugin()
    {
        Assert.Equal(HsoGallery.IMAGE_PREFIX, GalleryStore.IMAGE_PREFIX);
        Assert.Equal(HsoGallery.BUCKET_PURPOSE, GalleryStore.BUCKET_PURPOSE);
        Assert.Equal(BucketNames.Plugin("shinano-a", HsoGallery.BUCKET_PURPOSE), GalleryStore.BucketName("shinano-a"));
        Assert.Equal(ConfigKeys.DEFAULT_BUCKET_PREFIX, new DeploymentOptions().BucketPrefix);
        Assert.Equal((int)HsoImageOrigin.Tool, GalleryStore.ORIGIN_TOOL);
        Assert.Equal((int)HsoInfoState.Pending, GalleryStore.INFO_STATE_PENDING);
    }

    private static PropertyInfo Property(string name) => RecordProperties.Single(property => property.Name == name);

    private static SugarColumn Column(string propertyName) =>
        Property(propertyName).GetCustomAttribute<SugarColumn>() ?? throw new InvalidOperationException($"{propertyName} 没有 SugarColumn");
}
