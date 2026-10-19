using System.Data;
using NpgsqlTypes;
using SqlSugar;

namespace Shinano.Infrastructure.Database;

/// <summary>
/// 让 SqlSugar 以 jsonb 参数类型写入 string 属性。PostgreSQL 不会把 text 参数隐式转换为 jsonb，
/// 未指定时插入会失败（42804）。
/// </summary>
public sealed class JsonbStringConverter : ISugarDataConverter
{
    /// <inheritdoc />
    public SugarParameter ParameterConverter<T>(object columnValue, int columnIndex) =>
        new("@jsonb" + columnIndex, columnValue ?? DBNull.Value)
        {
            CustomDbType = NpgsqlDbType.Jsonb
        };

    /// <inheritdoc />
    public T QueryConverter<T>(IDataRecord dataRecord, int dataRecordIndex) =>
        // SAFE: 该转换器只用于 string? 属性，数据库 null 映射为 null。
        (T)(object?)(dataRecord.IsDBNull(dataRecordIndex) ? null : dataRecord.GetString(dataRecordIndex))!;
}
