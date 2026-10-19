using System.Data;
using NpgsqlTypes;
using SqlSugar;

namespace Shinano.Infrastructure.Database;

/// <summary>
/// 让 SqlSugar 以 PostgreSQL date 参数类型读写 <see cref="DateOnly" /> 属性。SqlSugar 默认把它当作
/// Kind=Unspecified 的 DateTime 传给 Npgsql，被映射为 timestamptz 后写入失败。
/// </summary>
/// <remarks>查询表达式中的日期须先放入局部变量，SqlSugar 不支持在表达式里直接构造 <see cref="DateOnly" />。</remarks>
public sealed class DateOnlySqlConverter : ISugarDataConverter
{
    /// <inheritdoc />
    public SugarParameter ParameterConverter<T>(object columnValue, int columnIndex) =>
        new("@date" + columnIndex, columnValue ?? DBNull.Value)
        {
            CustomDbType = NpgsqlDbType.Date
        };

    /// <inheritdoc />
    public T QueryConverter<T>(IDataRecord dataRecord, int dataRecordIndex) =>
        (T)(object)DateOnly.FromDateTime(dataRecord.GetDateTime(dataRecordIndex));
}
