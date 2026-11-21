using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shinano.Plugin.Gemini.Api;
using Shinano.Plugin.Gemini.Chat;
using Sora.Core.Types;

namespace Shinano.Tests.Plugins.Gemini;

/// <summary>Gemini 对话插件核心契约单元测试：模型能力表、REST 协议构造/解析、内存多轮与媒体上限、冷却与回复拼装。</summary>
public sealed class GeminiPluginTests
{
    [Fact]
    public void ModelCapabilities_MapsEffortsPerModelFamilyAndThinkingFlag()
    {
        Assert.Equal(["default", "low", "medium", "high", "xhigh"], GeminiModelCapabilities.GetAllowedEfforts("gemini-4-hydrogen"));
        Assert.Equal(["default", "low", "medium", "high", "xhigh"], GeminiModelCapabilities.GetAllowedEfforts("models/gemini-4-neon"));
        Assert.Equal(["default", "low", "medium", "high"], GeminiModelCapabilities.GetAllowedEfforts("gemini-4-carbon"));
        Assert.Equal(["default", "minimal", "low", "medium", "high"], GeminiModelCapabilities.GetAllowedEfforts("gemini-3.8-flash"));
        Assert.Equal(["default"], GeminiModelCapabilities.GetAllowedEfforts("gemini-2.5-pro"));
        Assert.Empty(GeminiModelCapabilities.GetAllowedEfforts("gemini-4-hydrogen", thinking: false));
        Assert.Equal(["default", "minimal", "low", "medium", "high", "xhigh"], GeminiModelCapabilities.GetAllowedEfforts("gemini-9-future", thinking: true));

        Assert.Equal("medium", GeminiModelCapabilities.NormalizeEffort("gemini-4-carbon", "xhigh"));
        Assert.Equal("default", GeminiModelCapabilities.NormalizeEffort("gemini-2.5-flash", "high"));
        Assert.Equal(string.Empty, GeminiModelCapabilities.NormalizeEffort("gemini-2.0-flash", "medium"));
    }

    [Fact]
    public void ApiClient_BuildsMultimodalThinkingSearchAndThoughtSignaturePayload()
    {
        GeminiGenerateRequest request = new()
        {
            BaseUrl            = "https://example.invalid",
            ApiKey             = "test-key",
            Model              = "gemini-4-hydrogen",
            Effort             = "medium",
            SystemPrompt       = "你是简洁助手",
            IncludeThoughts    = true,
            EnableGoogleSearch = true,
            MaxSearchSources   = 3,
            Messages =
            [
                new GeminiConversationMessage
                {
                    Role = "user",
                    Parts =
                    [
                        new GeminiConversationPart { Text = "看图" },
                        new GeminiConversationPart
                        {
                            Media = new GeminiMediaPart
                            {
                                MediaType = "image/png",
                                Data      = [1, 2, 3, 4]
                            }
                        }
                    ]
                },
                new GeminiConversationMessage
                {
                    Role = "model",
                    Parts =
                    [
                        new GeminiConversationPart
                        {
                            Text             = "这是一张测试图",
                            ThoughtSignature = "sig-abc-123"
                        }
                    ]
                }
            ]
        };

        JsonObject payload = GeminiApiClient.BuildGeneratePayload(request, "gemini-4-hydrogen");

        Assert.Equal("你是简洁助手", payload["systemInstruction"]?["parts"]?[0]?["text"]?.GetValue<string>());
        Assert.Equal("medium", payload["generationConfig"]?["thinkingConfig"]?["thinkingLevel"]?.GetValue<string>());
        Assert.True(payload["generationConfig"]?["thinkingConfig"]?["includeThoughts"]?.GetValue<bool>());
        Assert.NotNull(payload["tools"]?[0]?["google_search"]);
        Assert.Equal("image/png", payload["contents"]?[0]?["parts"]?[1]?["inlineData"]?["mimeType"]?.GetValue<string>());
        Assert.Equal("sig-abc-123", payload["contents"]?[1]?["parts"]?[0]?["thoughtSignature"]?.GetValue<string>());
    }

    [Fact]
    public void ApiClient_ParsesThoughtSummarySignatureAndDeduplicatedSources()
    {
        const string RESPONSE_JSON = """
            {
              "candidates": [
                {
                  "content": {
                    "role": "model",
                    "parts": [
                      { "thought": true, "text": "先分析用户问题", "thoughtSignature": "sig-from-thought" },
                      { "text": "最终回答内容" }
                    ]
                  },
                  "groundingMetadata": {
                    "groundingChunks": [
                      { "web": { "uri": "https://docs.example.com/a", "title": "文档 A" } },
                      { "web": { "uri": "https://docs.example.com/a", "title": "重复文档 A" } },
                      { "web": { "uri": "https://news.example.org/b", "title": "新闻 B" } },
                      { "web": { "uri": "https://blog.example.net/c", "title": "博客 C" } }
                    ]
                  }
                }
              ]
            }
            """;

        GeminiGenerateResponse parsed = GeminiApiClient.ParseGenerateResponse(RESPONSE_JSON, maxSearchSources: 2);

        Assert.Equal("最终回答内容", parsed.AnswerText);
        Assert.Equal("先分析用户问题", parsed.ThoughtSummary);
        Assert.Single(parsed.ModelParts);
        Assert.Equal("最终回答内容", parsed.ModelParts[0].Text);
        Assert.Equal("sig-from-thought", parsed.ModelParts[0].ThoughtSignature);
        Assert.Equal(2, parsed.Sources.Count);
        Assert.Equal("docs.example.com", parsed.Sources[0].Domain);
        Assert.Equal("news.example.org", parsed.Sources[1].Domain);
    }

    [Fact]
    public async Task ApiClient_ListModelsFiltersGenerateContentAndUsesHeaderKey()
    {
        string? capturedHeader = null;
        string? capturedUrl = null;
        using GeminiApiClient client = new(
            NullLogger<GeminiApiClient>.Instance,
            _ => new StubHttpMessageHandler(request =>
            {
                capturedUrl = request.RequestUri?.ToString();
                capturedHeader = request.Headers.TryGetValues("x-goog-api-key", out IEnumerable<string>? values)
                    ? string.Join(",", values)
                    : null;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "models": [
                            {
                              "name": "models/gemini-4-hydrogen",
                              "displayName": "Gemini 4 Hydrogen",
                              "thinking": true,
                              "supportedGenerationMethods": ["generateContent", "countTokens"]
                            },
                            {
                              "name": "models/text-embedding-004",
                              "displayName": "Embedding",
                              "supportedGenerationMethods": ["embedContent"]
                            }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }));

        IReadOnlyList<GeminiModelInfo> models = await client.ListModelsAsync(
            "https://api.example.test/v1beta/",
            string.Empty,
            "secret-token",
            CancellationToken.None);

        Assert.Equal("secret-token", capturedHeader);
        Assert.DoesNotContain("secret-token", capturedUrl ?? string.Empty);
        Assert.Single(models);
        Assert.Equal("gemini-4-hydrogen", models[0].Id);
        Assert.Contains("xhigh", models[0].AllowedEfforts);
    }

    [Fact]
    public void ConversationStore_IsolatesByGroupAndUserAndResetsStrictlyAfterLimit()
    {
        GeminiConversationStore store = new();
        GroupId groupA = 10001;
        UserId user1 = 20001;
        UserId user2 = 20002;

        // 群内发言者 A 第 1 轮
        Assert.True(store.TryBeginTurn(groupA, user1, maxTurns: 2, out GeminiTurnLease turn1));
        Assert.Equal(1, turn1.TurnNumber);
        Assert.Empty(turn1.History);
        // 并发请求被拒绝
        Assert.False(store.TryBeginTurn(groupA, user1, maxTurns: 2, out _));
        // 同群不同发言者 B 互不干扰
        Assert.True(store.TryBeginTurn(groupA, user2, maxTurns: 2, out GeminiTurnLease otherUser));
        store.AbortTurn(otherUser);

        store.CommitTurn(turn1, UserText("我叫小明"), ModelText("你好小明"));
        Assert.Equal(1, store.GetCompletedTurns(groupA, user1));

        // 群内发言者 A 第 2 轮（带第 1 轮历史）
        Assert.True(store.TryBeginTurn(groupA, user1, maxTurns: 2, out GeminiTurnLease turn2));
        Assert.Equal(2, turn2.TurnNumber);
        Assert.False(turn2.ResetPrevious);
        Assert.Equal(2, turn2.History.Count);
        store.CommitTurn(turn2, UserText("我叫什么"), ModelText("你叫小明"));

        // 第 3 次提问超出 2 轮上限：严格视为新对话，历史为空、轮数为 1
        Assert.True(store.TryBeginTurn(groupA, user1, maxTurns: 2, out GeminiTurnLease turn3));
        Assert.True(turn3.ResetPrevious);
        Assert.Equal(1, turn3.TurnNumber);
        Assert.Empty(turn3.History);

        // 若第 3 次失败取消，不应破坏原有状态
        store.AbortTurn(turn3);
        Assert.Equal(2, store.GetCompletedTurns(groupA, user1));

        // 重新发起并成功提交后，变为新对话第 1 轮
        Assert.True(store.TryBeginTurn(groupA, user1, maxTurns: 2, out GeminiTurnLease turn3Retry));
        store.CommitTurn(turn3Retry, UserText("重新开始"), ModelText("好的"));
        Assert.Equal(1, store.GetCompletedTurns(groupA, user1));

        // 清空上下文
        store.Clear(groupA, user1);
        Assert.Equal(0, store.GetCompletedTurns(groupA, user1));
    }

    [Fact]
    public void ConversationStore_TrimsEarliestMediaWhenExceedingBudget()
    {
        GeminiConversationStore store = new(maxSessionMediaBytes: 100);
        GroupId group = default;
        UserId user = 30001;

        Assert.True(store.TryBeginTurn(group, user, maxTurns: 8, out GeminiTurnLease t1));
        store.CommitTurn(
            t1,
            new GeminiConversationMessage
            {
                Role = "user",
                Parts =
                [
                    new GeminiConversationPart { Text = "图1" },
                    new GeminiConversationPart { Media = new GeminiMediaPart { MediaType = "image/png", Data = new byte[60] } }
                ]
            },
            ModelText("收到图1"));
        Assert.Equal(60, store.GetMediaBytes(group, user));

        Assert.True(store.TryBeginTurn(group, user, maxTurns: 8, out GeminiTurnLease t2));
        store.CommitTurn(
            t2,
            new GeminiConversationMessage
            {
                Role = "user",
                Parts =
                [
                    new GeminiConversationPart { Text = "图2" },
                    new GeminiConversationPart { Media = new GeminiMediaPart { MediaType = "image/png", Data = new byte[60] } }
                ]
            },
            ModelText("收到图2"));

        // 超出 100 字节上限后，最早一轮的 60 字节图片被裁剪，保留最新一轮的 60 字节图片。
        Assert.Equal(60, store.GetMediaBytes(group, user));
        Assert.True(store.TryBeginTurn(group, user, maxTurns: 8, out GeminiTurnLease t3));
        Assert.Equal(GeminiConversationStore.TRIMMED_MEDIA_PLACEHOLDER, t3.History[0].Parts[1].Text);
        Assert.Null(t3.History[0].Parts[1].Media);
        Assert.NotNull(t3.History[2].Parts[1].Media);
        store.AbortTurn(t3);
    }

    [Fact]
    public void CooldownTracker_IsGlobalPerUserDoesNotResetOnRetryAndRefundsOnFailure()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        GeminiCooldownTracker tracker = new(time);
        UserId user = 40001;
        TimeSpan window = TimeSpan.FromSeconds(30);

        Assert.True(tracker.TryAcquire(user, window, out CooldownTicket ticket1, out _));

        // 10 秒后重试被拒，但不重置计时。
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.False(tracker.TryAcquire(user, window, out _, out int remaining));
        Assert.Equal(20, remaining);

        // 再过 20 秒（共 30 秒）恢复可用。
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.True(tracker.TryAcquire(user, window, out CooldownTicket ticket2, out _));

        // 失败退还后立即可再次尝试。
        tracker.Refund(ticket2);
        Assert.True(tracker.TryAcquire(user, window, out _, out _));

        // 旧票据退还不会影响后续新记录。
        tracker.Refund(ticket1);
        Assert.False(tracker.TryAcquire(user, window, out _, out _));
    }

    [Fact]
    public void ReplyFormatterAndPrefixMatching_FollowDesignContract()
    {
        Assert.True(GeminiMessageHandler.TryMatchPrefix("# 你好", "#", out string prompt));
        Assert.Equal("你好", prompt);
        Assert.True(GeminiMessageHandler.TryMatchPrefix("#reset", "#", out string resetArg));
        Assert.True(GeminiMessageHandler.IsResetCommand(resetArg));
        Assert.True(GeminiMessageHandler.IsResetCommand("新对话"));
        Assert.False(GeminiMessageHandler.IsResetCommand("新对话 帮我写代码"));

        GeminiGenerateResponse response = new()
        {
            AnswerText     = "答案正文",
            ThoughtSummary = "思考一步\n思考二步",
            ModelParts     = [],
            Sources        = [new GeminiSearchSource { Title = "示例站点", Domain = "example.com" }]
        };

        string markdownWithAll = GeminiReplyFormatter.BuildMarkdown(response, includeThoughts: true, enableGoogleSearch: true);
        Assert.Contains("> **思考摘要**", markdownWithAll);
        Assert.Contains("### 参考来源", markdownWithAll);
        Assert.Contains("示例站点（example.com）", markdownWithAll);

        string markdownMinimal = GeminiReplyFormatter.BuildMarkdown(response, includeThoughts: false, enableGoogleSearch: false);
        Assert.Equal("答案正文", markdownMinimal);

        Assert.Equal("gemini-4-hydrogen · medium", GeminiReplyFormatter.BuildFooterLeft("gemini-4-hydrogen", "medium"));
        Assert.Equal("gemini-2.5-pro", GeminiReplyFormatter.BuildFooterLeft("gemini-2.5-pro", "default"));
        Assert.Equal("[1/2]", GeminiReplyFormatter.BuildFooterRight(1, 2));
    }

    private static GeminiConversationMessage UserText(string text) =>
        new()
        {
            Role  = "user",
            Parts = [new GeminiConversationPart { Text = text }]
        };

    private static GeminiConversationMessage ModelText(string text) =>
        new()
        {
            Role  = "model",
            Parts = [new GeminiConversationPart { Text = text }]
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
