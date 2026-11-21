using System.Text;
using Shinano.Framework.Rendering;
using Shinano.Plugin.Gemini.Api;

namespace Shinano.Plugin.Gemini.Chat;

/// <summary>拼装 Gemini 回复的 Markdown 文档、卡片页脚与纯文本降级文案。</summary>
internal static class GeminiReplyFormatter
{
    private const int PLAIN_TEXT_MAX_LENGTH = 1500;

    /// <summary>生成用于 <see cref="DocumentCard" /> 的完整 Markdown 文本。</summary>
    public static string BuildMarkdown(GeminiGenerateResponse response, bool includeThoughts, bool enableGoogleSearch)
    {
        StringBuilder builder = new();

        if (includeThoughts && !string.IsNullOrWhiteSpace(response.ThoughtSummary))
        {
            builder.AppendLine("> **思考摘要**");
            builder.AppendLine(">");
            string normalizedThoughts = response.ThoughtSummary.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            foreach (string line in normalizedThoughts.Split('\n'))
            {
                if (line.Length == 0) builder.AppendLine(">");
                else builder.Append("> ").AppendLine(line);
            }

            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
        }

        builder.Append(response.AnswerText.Trim());

        if (enableGoogleSearch && response.Sources.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
            builder.AppendLine("### 参考来源");
            builder.AppendLine();
            foreach (GeminiSearchSource source in response.Sources)
            {
                string label = string.IsNullOrEmpty(source.Domain) || string.Equals(source.Title, source.Domain, StringComparison.OrdinalIgnoreCase)
                    ? source.Title
                    : $"{source.Title}（{source.Domain}）";
                builder.Append("- ").AppendLine(label);
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>生成卡片页脚左侧文字：模型名与 effort（禁用或默认时不附 effort）。</summary>
    public static string BuildFooterLeft(string model, string effort)
    {
        string normalizedModel = GeminiModelCapabilities.NormalizeModelId(model);
        if (GeminiModelCapabilities.IsExplicitThinkingLevel(effort))
            return $"{normalizedModel} · {effort.ToLowerInvariant()}";
        return normalizedModel;
    }

    /// <summary>生成卡片页脚右侧轮数文字，如 <c>[1/2]</c>。</summary>
    public static string BuildFooterRight(int turnNumber, int turnLimit) => $"[{turnNumber}/{turnLimit}]";

    /// <summary>渲染或图片发送失败时的纯文本降级文案。</summary>
    public static string BuildPlainTextFallback(string markdown, string footerLeft, string footerRight)
    {
        string body = markdown.Length > PLAIN_TEXT_MAX_LENGTH
            ? markdown[..PLAIN_TEXT_MAX_LENGTH].TrimEnd() + "\n……（内容过长，卡片发送失败已截断）"
            : markdown;
        return $"{body}\n\n[{footerLeft}] {footerRight}";
    }
}
