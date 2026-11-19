namespace Shinano.Plugin.Qa.Panel;

/// <summary>面板中的一条词条。</summary>
public sealed class QaEntryView
{
    /// <summary>词条 ID。</summary>
    public required long Id { get; init; }

    /// <summary>所属群。</summary>
    public required long GroupId { get; init; }

    /// <summary>匹配方式。</summary>
    public required QaMatchType MatchType { get; init; }

    /// <summary>问题文本或正则表达式。</summary>
    public required string Question { get; init; }

    /// <summary>回答片段；媒体经面板 API 读取。</summary>
    public required IReadOnlyList<QaAnswerPart> Answer { get; init; }

    /// <summary>录入人 QQ 号；面板录入为 0。</summary>
    public required long CreatedBy { get; init; }

    /// <summary>创建时间（UTC）。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>版本；面板确认覆盖时回传，用于判断是否已被他人修改。</summary>
    public required int Version { get; init; }

    /// <summary>由快照创建。</summary>
    public static QaEntryView From(QaEntry entry) =>
        new()
        {
            Id        = entry.Id,
            GroupId   = entry.GroupId,
            MatchType = entry.MatchType,
            Question  = entry.Question,
            Answer    = entry.Answer,
            CreatedBy = entry.CreatedBy,
            CreatedAt = entry.CreatedAt,
            Version   = entry.Version
        };
}

/// <summary>面板选群用的群信息。</summary>
public sealed class QaGroupView
{
    /// <summary>群号。</summary>
    public required long GroupId { get; init; }

    /// <summary>群名。</summary>
    public required string Name { get; init; }
}

/// <summary>新增禁止提问词的请求。</summary>
public sealed class QaForbiddenRequest
{
    /// <summary>不能作为问题的文本。</summary>
    public string Question { get; init; } = string.Empty;
}

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

/// <summary>面板新增词条时回答片段的来源。</summary>
public enum QaUploadPartType
{
    /// <summary>文字。</summary>
    Text,

    /// <summary>@ 某个 QQ 号。</summary>
    Mention,

    /// <summary>上传的文件，按内容类型识别为图片、语音、视频或文件。</summary>
    Upload
}
