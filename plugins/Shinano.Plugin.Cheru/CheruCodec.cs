using System.Text;
using System.Text.RegularExpressions;

namespace Shinano.Plugin.Cheru;

/// <summary>
/// 切噜语编解码。每段连续的非空白字符整体以 GB18030 编码为一个切噜词：每个字节先低 4 位、后高 4 位映射到 16 个切噜字，
/// 词首加"切"，空白原样保留。切噜词格式与 HoshinoBot 相同，双方的解码器可以互相解出原文；与 HoshinoBot 不同的是
/// 字母、数字、网址与标点同样编码，不留原文。
/// </summary>
public static partial class CheruCodec
{
    private const string CHERU_SET      = "切卟叮咧哔唎啪啰啵嘭噜噼巴拉蹦铃";
    private const char   WORD_HEAD      = '切';
    private const int    NIBBLE_BITS    = 4;
    private const int    NIBBLE_MASK    = 0x0F;
    private const int    CHARS_PER_BYTE = 2;
    private const string GB18030        = "GB18030";

    private static readonly Encoding Gb18030 = CreateEncoding();

    /// <summary>把文本中每段连续的非空白字符编码为一个切噜词，空白原样保留。</summary>
    public static string Encode(string text) => NonWhitespace().Replace(text, match => ToCheru(match.Value));

    /// <summary>把文本中完整的切噜词解码，其余原样保留。</summary>
    public static string Decode(string text) =>
        string.Concat(WordBoundary().Split(text).Select(word => CheruWord().IsMatch(word) ? FromCheru(word) : word));

    private static string ToCheru(string word)
    {
        StringBuilder result = new();
        result.Append(WORD_HEAD);
        foreach (byte value in Gb18030.GetBytes(word))
        {
            result.Append(CHERU_SET[value & NIBBLE_MASK]);
            result.Append(CHERU_SET[(value >> NIBBLE_BITS) & NIBBLE_MASK]);
        }

        return result.ToString();
    }

    // 两个切噜字合成一个字节；长度为奇数时末尾落单的字不对应任何字节，原样保留。
    private static string FromCheru(string word)
    {
        string content = word[1..];
        int pairs = content.Length / CHARS_PER_BYTE;
        byte[] bytes = new byte[pairs];
        for (int index = 0; index < pairs; index++)
        {
            int low = CHERU_SET.IndexOf(content[index * CHARS_PER_BYTE]);
            int high = CHERU_SET.IndexOf(content[index * CHARS_PER_BYTE + 1]);
            bytes[index] = (byte)(low | (high << NIBBLE_BITS));
        }

        string decoded = Gb18030.GetString(bytes);
        return content.Length % CHARS_PER_BYTE == 0 ? decoded : decoded + content[^1];
    }

    private static Encoding CreateEncoding()
    {
        // .NET 默认只提供 Unicode 系列编码，GB18030 需要注册代码页编码提供程序。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(GB18030);
    }

    [GeneratedRegex(@"\b")]
    private static partial Regex WordBoundary();

    [GeneratedRegex(@"\S+")]
    private static partial Regex NonWhitespace();

    [GeneratedRegex($"^切[{CHERU_SET}]+$")]
    private static partial Regex CheruWord();
}
