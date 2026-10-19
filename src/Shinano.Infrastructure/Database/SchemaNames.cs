using System.Text.RegularExpressions;

namespace Shinano.Infrastructure.Database;

/// <summary>
/// 宿主使用的 schema 名称：全局数据与机器人账号数据分开存放。
/// </summary>
public sealed partial class SchemaNames
{
    /// <summary>全局 schema，默认 <c>global</c>。</summary>
    public string Global { get; init; } = "global";

    /// <summary>机器人账号 schema 前缀，默认 <c>user_</c>，完整名称为前缀加 <c>self_id</c>。</summary>
    public string AccountPrefix { get; init; } = "user_";

    /// <summary>返回机器人账号 <paramref name="selfId" /> 的 schema 名称。</summary>
    public string Account(long selfId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(selfId);
        return AccountPrefix + selfId;
    }

    /// <summary>
    /// 校验 schema 或表名只含小写字母、数字与下划线，可以安全地拼入 DDL。
    /// </summary>
    /// <exception cref="ArgumentException">名称不合法。</exception>
    public static string RequireIdentifier(string name)
    {
        if (!IdentifierPattern().IsMatch(name)) throw new ArgumentException($"非法的标识符：{name}", nameof(name));
        return name;
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex IdentifierPattern();
}
