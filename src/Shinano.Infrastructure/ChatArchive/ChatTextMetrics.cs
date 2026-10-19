using System.Globalization;
using System.Text;

namespace Shinano.Infrastructure.ChatArchive;

/// <summary>聊天字数规则。</summary>
public static class ChatTextMetrics
{
    /// <summary>
    /// 按用户可见字符计数：以扩展字素簇为单位，完整 emoji（含组合序列、肤色与国旗）计为 1；
    /// 空白与控制字符不计入。
    /// </summary>
    public static int CountVisibleCharacters(string text)
    {
        int count = 0;
        int index = 0;
        while (index < text.Length)
        {
            int length = StringInfo.GetNextTextElementLength(text, index);
            Rune first = Rune.GetRuneAt(text, index);
            if (!Rune.IsWhiteSpace(first) && !Rune.IsControl(first)) count++;
            index += length;
        }

        return count;
    }
}
