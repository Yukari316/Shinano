using System.Text.RegularExpressions;

namespace Shinano.Infrastructure.Storage;

/// <summary>
/// 部署内 bucket 命名：宿主的聊天媒体与持久媒体 bucket，以及插件自有 bucket，均为 <c>&lt;prefix&gt;-&lt;用途&gt;</c>。
/// </summary>
public static partial class BucketNames
{
    /// <summary>聊天媒体 bucket 的用途名。</summary>
    public const string CHAT_PURPOSE = "chat";

    /// <summary>持久媒体 bucket 的用途名。</summary>
    public const string MEDIA_PURPOSE = "media";

    private const int MAX_BUCKET_NAME_LENGTH = 63;

    /// <summary>聊天媒体 bucket 名。</summary>
    public static string Chat(string prefix) => Compose(prefix, CHAT_PURPOSE);

    /// <summary>持久媒体 bucket 名。</summary>
    public static string Media(string prefix) => Compose(prefix, MEDIA_PURPOSE);

    /// <summary>
    /// 插件自有 bucket 名。用途名只能含小写字母、数字与连字符，且不能与宿主用途重名。
    /// </summary>
    /// <exception cref="ArgumentException">用途名不合法或与宿主保留用途相同。</exception>
    public static string Plugin(string prefix, string purpose)
    {
        if (purpose is CHAT_PURPOSE or MEDIA_PURPOSE)
            throw new ArgumentException($"用途名 {purpose} 为宿主保留", nameof(purpose));
        return Compose(prefix, purpose);
    }

    private static string Compose(string prefix, string purpose)
    {
        string name = $"{prefix}-{purpose}";
        if (name.Length > MAX_BUCKET_NAME_LENGTH || !BucketPattern().IsMatch(name))
            throw new ArgumentException($"非法的 bucket 名：{name}");
        return name;
    }

    // S3 bucket 命名规则的子集：小写字母、数字、连字符，以字母或数字开头和结尾，至少 3 个字符。
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$")]
    private static partial Regex BucketPattern();
}
