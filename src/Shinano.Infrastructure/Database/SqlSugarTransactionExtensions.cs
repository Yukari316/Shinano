using SqlSugar;

namespace Shinano.Infrastructure.Database;

/// <summary>SqlSugar 事务的辅助方法。</summary>
public static class SqlSugarTransactionExtensions
{
    /// <summary>
    /// 在事务中执行 <paramref name="action" />：成功则提交并返回结果，失败则回滚并重新抛出原异常。
    /// </summary>
    public static async Task<T> InTransactionAsync<T>(this ISqlSugarClient client, Func<Task<T>> action)
    {
        DbResult<T> result = await client.Ado.UseTranAsync(action);
        if (result.IsSuccess) return result.Data;

        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(result.ErrorException);
        return default!; // SAFE: 上一行总会抛出，此处不可达。
    }
}
