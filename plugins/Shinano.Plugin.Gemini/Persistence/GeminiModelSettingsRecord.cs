using SqlSugar;

namespace Shinano.Plugin.Gemini.Persistence;

/// <summary>Gemini 当前选用的模型与思考深度配置，位于全局 schema，不按机器人账号区分。</summary>
[SugarTable("gemini_model_settings")]
public sealed class GeminiModelSettingsRecord
{
    /// <summary>单行配置主键。</summary>
    public const int SINGLETON_ID = 1;

    /// <summary>主键，固定为 <see cref="SINGLETON_ID" />。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public int Id { get; set; } = SINGLETON_ID;

    /// <summary>模型 ID（不含 <c>models/</c> 前缀）。</summary>
    [SugarColumn(ColumnName = "model", Length = 128)]
    public string Model { get; set; } = "gemini-4-hydrogen";

    /// <summary>思考深度（<c>default</c>、<c>minimal</c>、<c>low</c>、<c>medium</c>、<c>high</c>、<c>xhigh</c> 或空字符串表示禁用）。</summary>
    [SugarColumn(ColumnName = "effort", Length = 32)]
    public string Effort { get; set; } = "medium";

    /// <summary>可选多行系统提示词（System Instruction）；留空时不发送。</summary>
    [SugarColumn(ColumnName = "system_prompt", ColumnDataType = "text", IsNullable = true)]
    public string? SystemPrompt { get; set; } = string.Empty;
}
