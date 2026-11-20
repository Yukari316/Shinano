using System.Text.RegularExpressions;

namespace Shinano.Plugin.NB2Bridge.PluginPackages;

/// <summary>
/// 管理员约束文件（<c>constraints.txt</c>）的校验。内容会作为 <c>uv pip install -c</c> 的约束文件，只允许「包名 + 版本条件
/// + 可选的环境标记」与 <c>#</c> 注释：以 <c>-</c> 开头的选项行（如 <c>--index-url</c>、<c>-r</c>）与直接引用（URL、本地路径）
/// 会改变下载来源或读取其他文件，一律拒绝。
/// </summary>
public static partial class Nb2Constraints
{
    /// <summary>约束文件的长度上限（字符）。</summary>
    public const int MAX_LENGTH = 64 * 1024;

    /// <summary>校验约束文本。</summary>
    /// <returns>第一处错误的说明；有效时为 null。</returns>
    public static string? Validate(string text)
    {
        if (text.Length > MAX_LENGTH) return $"约束文件不能超过 {MAX_LENGTH} 个字符";

        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = StripComment(lines[index]).Trim();
            if (line.Length == 0) continue;
            if (!ConstraintPattern().IsMatch(line))
                return $"第 {index + 1} 行不是有效的约束：每行写一个包名与版本条件（如 tarina<0.8），不能包含选项、URL 或路径";
        }

        return null;
    }

    // requirements 格式中，行首或空白之后的 # 开始注释。
    private static string StripComment(string line)
    {
        int start = line.StartsWith('#') ? 0 : line.IndexOf(" #", StringComparison.Ordinal);
        return start < 0 ? line : line[..start];
    }

    // 包名、可选 extras、可选的若干个以逗号分隔的版本条件、可选的 ; 环境标记。
    [GeneratedRegex(
        @"^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?(?:\[[A-Za-z0-9._,\s-]*\])?\s*(?:(?:===|==|!=|~=|<=|>=|<|>)\s*[A-Za-z0-9.*+!_-]+(?:\s*,\s*(?:===|==|!=|~=|<=|>=|<|>)\s*[A-Za-z0-9.*+!_-]+)*)?\s*(?:;[^@]*)?$")]
    private static partial Regex ConstraintPattern();
}
