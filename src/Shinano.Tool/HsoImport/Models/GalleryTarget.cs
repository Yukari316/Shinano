using Npgsql;

namespace Shinano.Tool.HsoImport.Models;

/// <summary>导入目标：图库表所在的数据库与插件 bucket。含凭据，不得输出或记录。</summary>
public sealed class GalleryTarget
{
    /// <summary>数据库连接信息。</summary>
    public required NpgsqlConnectionStringBuilder Database { get; init; }

    /// <summary>RustFS 内部地址。</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>RustFS AccessKey。</summary>
    public required string AccessKey { get; init; }

    /// <summary>RustFS SecretKey。</summary>
    public required string SecretKey { get; init; }

    /// <summary>Hso 插件的 bucket 名。</summary>
    public required string Bucket { get; init; }
}
