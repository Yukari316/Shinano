using System.Net;
using System.Text.RegularExpressions;

namespace Shinano.Tool.Setup;

/// <summary>初始化问答的输入校验：返回错误原因，合法时返回 null。规则与宿主、监控读取配置时的要求一致。</summary>
public static partial class InputRules
{
    private const int    MIN_PORT              = 1;
    private const int    MAX_PORT              = 65535;
    private const int    MAX_BUCKET_NAME       = 63;
    private const int    MAX_IDENTIFIER_LENGTH = 63;
    private const string LONGEST_HOST_BUCKET   = "-media";
    private const string LOCALHOST             = "localhost";

    /// <summary>时区 ID（IANA 或 Windows）。</summary>
    public static string? TimeZone(string value) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(value, out _) ? null : $"找不到时区 {value}";

    /// <summary>bucket 前缀：与宿主用途名组合后须是合法的 bucket 名。</summary>
    public static string? BucketPrefix(string value)
    {
        string name = value + LONGEST_HOST_BUCKET;
        return name.Length <= MAX_BUCKET_NAME && BucketPattern().IsMatch(name)
            ? null
            : "只能包含小写字母、数字与连字符，以字母或数字开头和结尾，且不超过 57 个字符";
    }

    /// <summary>逗号分隔的 Super User ID，至少一个。</summary>
    public static string? SuperUsers(string value) =>
        ParseIds(value) is { Length: > 0 } ? null : "请输入至少一个 QQ 号，多个用逗号分隔";

    /// <summary>解析逗号分隔的 ID；任何一项无效时为 null。</summary>
    public static long[]? ParseIds(string value)
    {
        string[] parts = value.Split([',', '，', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<long> ids = [];
        foreach (string part in parts)
        {
            if (!long.TryParse(part, out long id) || id <= 0) return null;
            if (!ids.Contains(id)) ids.Add(id);
        }

        return [.. ids];
    }

    /// <summary>端口。</summary>
    public static string? Port(string value) =>
        int.TryParse(value, out int port) && port is >= MIN_PORT and <= MAX_PORT ? null : $"端口须为 {MIN_PORT}–{MAX_PORT}";

    /// <summary>监听地址：IP 字面量或 <c>localhost</c>，不接受全部网卡地址。</summary>
    public static string? ListenAddress(string value)
    {
        if (value.Equals(LOCALHOST, StringComparison.OrdinalIgnoreCase)) return null;
        if (!IPAddress.TryParse(value, out IPAddress? address)) return "请输入 IP 地址或 localhost";
        return address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ? "不允许监听全部网卡地址，请填写具体 IP" : null;
    }

    /// <summary>http 或 https 的绝对地址。</summary>
    public static string? HttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" ? null : "请输入 http:// 或 https:// 开头的地址";

    /// <summary>主机名或 IP。</summary>
    public static string? HostName(string value) =>
        Uri.CheckHostName(value) == UriHostNameType.Unknown ? "请输入主机名或 IP 地址" : null;

    /// <summary>PostgreSQL 数据库名或角色名：只接受不需要引号的普通标识符。</summary>
    public static string? Identifier(string value) =>
        value.Length <= MAX_IDENTIFIER_LENGTH && IdentifierPattern().IsMatch(value) ? null : "只能包含字母、数字与下划线，且不能以数字开头";

    /// <summary>服务名。</summary>
    public static string? ServiceName(string value) =>
        ServiceNamePattern().IsMatch(value) ? null : "只能包含字母、数字、连字符与下划线，且以字母开头";

    // 与宿主 bucket 命名规则相同。
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$")]
    private static partial Regex BucketPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,79}$")]
    private static partial Regex ServiceNamePattern();
}
