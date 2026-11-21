namespace Shinano.Tool.Plugins;

/// <summary>
/// 语义化版本（SemVer 2.0）解析与比较，用于主程序和随包插件的版本排序。
/// 兼容缺少修订号的两段写法（<c>主.次</c> 视为 <c>主.次.0</c>），比较时忽略 <c>+</c> 之后的构建元数据。
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private const char BUILD_PREFIX      = '+';
    private const char PRERELEASE_PREFIX = '-';
    private const char DOT               = '.';
    private const char ZERO              = '0';

    private readonly string[] _prerelease;

    private SemanticVersion(int major, int minor, int patch, string[] prerelease)
    {
        Major       = major;
        Minor       = minor;
        Patch       = patch;
        _prerelease = prerelease;
    }

    /// <summary>主版本号。</summary>
    public int Major { get; }

    /// <summary>次版本号。</summary>
    public int Minor { get; }

    /// <summary>修订号。</summary>
    public int Patch { get; }

    /// <summary>预发布标识符序列；正式版本为空。</summary>
    public IReadOnlyList<string> Prerelease => _prerelease;

    /// <summary>尝试按 SemVer 2.0 规则解析版本字符串。</summary>
    public static bool TryParse(string? text, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string coreAndPrerelease = text.Trim();
        int buildIndex = coreAndPrerelease.IndexOf(BUILD_PREFIX);
        if (buildIndex >= 0)
        {
            string build = coreAndPrerelease[(buildIndex + 1)..];
            if (!IsValidIdentifierSequence(build, rejectNumericLeadingZero: false)) return false;
            coreAndPrerelease = coreAndPrerelease[..buildIndex];
        }

        string[] prerelease = [];
        int prereleaseIndex = coreAndPrerelease.IndexOf(PRERELEASE_PREFIX);
        if (prereleaseIndex >= 0)
        {
            string rawPrerelease = coreAndPrerelease[(prereleaseIndex + 1)..];
            if (!IsValidIdentifierSequence(rawPrerelease, rejectNumericLeadingZero: true)) return false;
            prerelease = rawPrerelease.Split(DOT);
            coreAndPrerelease = coreAndPrerelease[..prereleaseIndex];
        }

        string[] core = coreAndPrerelease.Split(DOT);
        if (core.Length is < 2 or > 3) return false;
        if (!TryParseCoreSegment(core[0], out int major) || !TryParseCoreSegment(core[1], out int minor)) return false;

        int patch = 0;
        if (core.Length == 3 && !TryParseCoreSegment(core[2], out patch)) return false;

        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    /// <summary>比较两个版本字符串；任一无法解析时返回 null。</summary>
    public static int? Compare(string? left, string? right) =>
        TryParse(left, out SemanticVersion? parsedLeft) && TryParse(right, out SemanticVersion? parsedRight)
            ? parsedLeft!.CompareTo(parsedRight)
            : null;

    /// <inheritdoc />
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;

        int result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        if (_prerelease.Length == 0 && other._prerelease.Length == 0) return 0;
        if (_prerelease.Length == 0) return 1;
        if (other._prerelease.Length == 0) return -1;

        int count = Math.Min(_prerelease.Length, other._prerelease.Length);
        for (int index = 0; index < count; index++)
        {
            result = ComparePrereleaseSegment(_prerelease[index], other._prerelease[index]);
            if (result != 0) return result;
        }

        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    /// <inheritdoc />
    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Major);
        hash.Add(Minor);
        hash.Add(Patch);
        foreach (string segment in _prerelease) hash.Add(segment, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private static bool TryParseCoreSegment(string segment, out int value)
    {
        value = 0;
        if (segment.Length == 0 || !IsDigits(segment)) return false;
        if (segment.Length > 1 && segment[0] == ZERO) return false;
        return int.TryParse(segment, out value);
    }

    private static bool IsValidIdentifierSequence(string text, bool rejectNumericLeadingZero)
    {
        if (text.Length == 0) return false;
        foreach (string segment in text.Split(DOT))
        {
            if (segment.Length == 0) return false;
            foreach (char character in segment)
                if (!char.IsAsciiLetterOrDigit(character) && character != PRERELEASE_PREFIX)
                    return false;
            if (rejectNumericLeadingZero && IsDigits(segment) && segment.Length > 1 && segment[0] == ZERO)
                return false;
        }

        return true;
    }

    private static int ComparePrereleaseSegment(string left, string right)
    {
        bool leftNumeric = IsDigits(left);
        bool rightNumeric = IsDigits(right);
        if (leftNumeric && rightNumeric)
        {
            // 纯数字预发布段已禁止前导零，先按位数再按字典序比较与数值大小完全等价且不溢出。
            int lengthOrder = left.Length.CompareTo(right.Length);
            return lengthOrder != 0 ? lengthOrder : string.CompareOrdinal(left, right);
        }

        if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
        return string.CompareOrdinal(left, right);
    }

    private static bool IsDigits(string text)
    {
        foreach (char character in text)
            if (!char.IsAsciiDigit(character))
                return false;
        return text.Length > 0;
    }
}
