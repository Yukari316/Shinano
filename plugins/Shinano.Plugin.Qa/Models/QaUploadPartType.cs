namespace Shinano.Plugin.Qa.Models;

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
