namespace Shinano.Plugin.Qa.Models;

/// <summary>词条的匹配方式，数值存入数据库。</summary>
public enum QaMatchType
{
    /// <summary>去掉首尾空白后与问题完全相同。</summary>
    Full = 0,

    /// <summary>正则表达式匹配。</summary>
    Regex = 1
}
