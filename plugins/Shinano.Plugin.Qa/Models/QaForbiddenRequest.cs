namespace Shinano.Plugin.Qa.Models;

/// <summary>新增禁止提问词的请求。</summary>
public sealed class QaForbiddenRequest
{
    /// <summary>不能作为问题的文本。</summary>
    public string Question { get; init; } = string.Empty;
}
