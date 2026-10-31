namespace Shinano.Plugin.Qa.Models;

/// <summary>录入消息无法解析的原因。</summary>
public enum QaParseError
{
    /// <summary>解析成功。</summary>
    None,

    /// <summary>问题或回答为空，或格式不符。</summary>
    Malformed,

    /// <summary>问题中夹带了文字以外的内容。</summary>
    QuestionNotText,

    /// <summary>含有不支持的消息段。</summary>
    Unsupported
}
