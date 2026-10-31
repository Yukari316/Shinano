namespace Shinano.Plugin.Qa.Models;

/// <summary>面板新增词条时的一段回答，按顺序组成完整回答。</summary>
public sealed class QaUploadPart
{
    /// <summary>来源。</summary>
    public required QaUploadPartType Type { get; init; }

    /// <summary>文字，仅 <see cref="QaUploadPartType.Text" />。</summary>
    public string? Text { get; init; }

    /// <summary>QQ 号，仅 <see cref="QaUploadPartType.Mention" />。</summary>
    public long? UserId { get; init; }

    /// <summary>表单中文件字段的名称，仅 <see cref="QaUploadPartType.Upload" />。</summary>
    public string? Field { get; init; }
}
