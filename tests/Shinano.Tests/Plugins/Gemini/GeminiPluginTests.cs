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
    private static readonly TimeSpan ConversationTimeout = TimeSpan.FromMinutes(30);

    [Fact]
    public void ModelCapabilities_MapsEffortsPerModelFamilyAndThinkingFlag()
    {
        Assert.Equal(["default", "low", "medium", "high", "xhigh"], GeminiModelCapabilities.GetAllowedEfforts("gemini-4-hydrogen"));
        Assert.Equal(["default", "low", "medium", "high", "xhigh"], GeminiModelCapabilities.GetAllowedEfforts("models/gemini-4-neon"));
        Assert.Equal(["default", "low", "medium", "high"], GeminiModelCapabilities.GetAllowedEfforts("gemini-4-carbon"));
        // 官方 thinking 文档：3.8 Flash 与 3.1 Pro 不支持 minimal，3.1 Flash-Lite Image 只有 minimal 与 high。
        Assert.Equal(["default", "low", "medium", "high"], GeminiModelCapabilities.GetAllowedEfforts("gemini-3.8-flash"));
        Assert.Equal(["default", "low", "medium", "high"], GeminiModelCapabilities.GetAllowedEfforts("gemini-3.1-pro-preview"));
        Assert.Equal(["default", "minimal", "high"], GeminiModelCapabilities.GetAllowedEfforts("gemini-3.1-flash-lite-image"));
        Assert.Equal(["default", "minimal", "low", "medium", "high"], GeminiModelCapabilities.GetAllowedEfforts("gemini-3.6-flash"));
        Assert.Equal(["default", "minimal", "low", "medium", "high"], GeminiModelCapabilities.GetAllowedEfforts("gemini-3.1-flash-lite"));
        Assert.Equal(["default"], GeminiModelCapabilities.GetAllowedEfforts("gemini-2.5-pro"));
        Assert.Empty(GeminiModelCapabilities.GetAllowedEfforts("gemini-4-hydrogen", thinking: false));
        Assert.Equal(["default", "minimal", "low", "medium", "high", "xhigh"], GeminiModelCapabilities.GetAllowedEfforts("gemini-9-future", thinking: true));

        Assert.Equal("medium", GeminiModelCapabilities.NormalizeEffort("gemini-4-carbon", "xhigh"));
        Assert.Equal("default", GeminiModelCapabilities.NormalizeEffort("gemini-2.5-flash", "high"));
        Assert.Equal(string.Empty, GeminiModelCapabilities.NormalizeEffort("gemini-2.0-flash", "medium"));
        Assert.Equal(["default", "minimal", "low", "medium", "high", "xhigh"], GeminiModelCapabilities.GetAllowedEfforts("gemini-2.0-flash-thinking", thinking: true));
        Assert.True(GeminiModelCapabilities.IsExplicitThinkingLevel("MEDIUM"));
        Assert.False(GeminiModelCapabilities.IsExplicitThinkingLevel("default"));
        Assert.False(GeminiModelCapabilities.IsExplicitThinkingLevel(null));
    }

    [Fact]
    public void Pricing_MatchesLongestPrefixLongContextTierAndPriceChanges()
    {
        DateOnly today = new(2026, 11, 22);

        // 具体前缀优先于泛化前缀，不依赖规则排列顺序。
        Assert.True(GeminiPricing.TryGetRates("gemini-2.5-flash-lite-preview", 1_000, today, out GeminiPricing.ModelRates flashLite25));
        Assert.Equal(0.10m, flashLite25.InputPerMillion);
        Assert.True(GeminiPricing.TryGetRates("gemini-2.5-flash", 1_000, today, out GeminiPricing.ModelRates flash25));
        Assert.Equal(0.30m, flash25.InputPerMillion);
        Assert.True(GeminiPricing.TryGetRates("gemini-3.1-flash-lite", 1_000, today, out GeminiPricing.ModelRates flashLite31));
        Assert.Equal(0.25m, flashLite31.InputPerMillion);
        Assert.True(GeminiPricing.TryGetRates("gemini-3.6-flash", 1_000, today, out GeminiPricing.ModelRates flash36));
        Assert.Equal(0.75m, flash36.InputPerMillion);
        Assert.Equal(3.75m, flash36.OutputPerMillion);

        // 官方定价页未列出的模型不猜价格。
        Assert.False(GeminiPricing.TryGetRates("gemini-3.0-preview", 1_000, today, out _));

        // 已公布的调价自生效日（含）起使用新单价。
        Assert.True(GeminiPricing.TryGetRates("gemini-4-carbon", 1_000, new DateOnly(2026, 11, 30), out GeminiPricing.ModelRates carbonBefore));
        Assert.Equal(2.00m, carbonBefore.InputPerMillion);
        Assert.True(GeminiPricing.TryGetRates("gemini-4-argon", 1_000, new DateOnly(2026, 12, 1), out GeminiPricing.ModelRates argonAfter));
        Assert.Equal(4.00m, argonAfter.InputPerMillion);
        Assert.Equal(20.00m, argonAfter.OutputPerMillion);
        Assert.True(GeminiPricing.TryGetRates("gemini-3.8-flash", 1_000, new DateOnly(2027, 1, 1), out GeminiPricing.ModelRates flash38After));
        Assert.Equal(1.50m, flash38After.InputPerMillion);

        // 超过 200K 上下文自动切换至长上下文阶梯价。
        Assert.True(GeminiPricing.TryGetRates("gemini-3.1-pro-preview", 200_000, today, out GeminiPricing.ModelRates pro31Standard));
        Assert.Equal(2.00m, pro31Standard.InputPerMillion);
        Assert.True(GeminiPricing.TryGetRates("gemini-3.1-pro-preview", 200_001, today, out GeminiPricing.ModelRates pro31Long));
        Assert.Equal(4.00m, pro31Long.InputPerMillion);
        Assert.Equal(18.00m, pro31Long.OutputPerMillion);
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
    public void ApiClient_OmitsThinkingConfigWhenEffortIsDisabled()
    {
        // 模型列表标记 thinking=false 的模型保存为空 effort；即使模型名未知（按规则会回退全部档位）且开启了思考摘要，也不发送 thinkingConfig。
        GeminiGenerateRequest request = new()
        {
            BaseUrl         = "https://example.invalid",
            ApiKey          = "test-key",
            Model           = "gemma-test-model",
            Effort          = string.Empty,
            IncludeThoughts = true,
            Messages        = [UserText("你好")]
        };

        JsonObject payload = GeminiApiClient.BuildGeneratePayload(request, "gemma-test-model");

        Assert.Null(payload["generationConfig"]);
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
    public void ApiClient_ParsesUsageMetadataCacheAndEstimatesCost()
    {
        const string RESPONSE_JSON = """
            {
              "candidates": [
                {
                  "content": {
                    "role": "model",
                    "parts": [{ "text": "测试回答" }]
                  }
                }
              ],
              "usageMetadata": {
                "promptTokenCount": 1000,
                "cachedContentTokenCount": 400,
                "candidatesTokenCount": 300,
                "thoughtsTokenCount": 200,
                "totalTokenCount": 1500
              }
            }
            """;

        TimeSpan elapsed = TimeSpan.FromSeconds(10);
        GeminiGenerateResponse parsed = GeminiApiClient.ParseGenerateResponse(RESPONSE_JSON, maxSearchSources: 5, elapsed);

        Assert.NotNull(parsed.Usage);
        Assert.Equal(1000, parsed.Usage.PromptTokens);
        Assert.Equal(400, parsed.Usage.CachedTokens);
        Assert.Equal(300, parsed.Usage.CandidatesTokens);
        Assert.Equal(200, parsed.Usage.ThoughtsTokens);
        Assert.Equal(1500, parsed.Usage.TotalTokens);
        Assert.Equal(500, parsed.Usage.OutputTokens);
        Assert.Equal(50.0, parsed.Usage.TokensPerSecond(elapsed), 1);

        // gemini-4-hydrogen: 未缓存输入 600 * 1.5 + 缓存输入 400 * 0.075 + 输出(含思考) 500 * 7.5 = 4680 / 1,000,000 = $0.00468
        DateOnly billingDate = new(2026, 11, 22);
        decimal cost = GeminiPricing.EstimateCostUsd("gemini-4-hydrogen", parsed.Usage, billingDate);
        Assert.Equal(0.00468m, cost);
        Assert.Equal(0m, GeminiPricing.EstimateCostUsd("unknown-custom-model", parsed.Usage, billingDate));
        Assert.Equal("输入 1000（缓存 400） / 输出 300 / 思考 200 / 总计 1500 tokens，速度 50.0 tok/s，费用 $0.0047", parsed.Usage.FormatSummary(elapsed, cost));
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
    public void ConversationStore_IsolatesByGroupAndUserAndStartsOverAfterLimit()
    {
        GeminiConversationStore store = new(new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        GroupId groupA = 10001;
        UserId user1 = 20001;
        UserId user2 = 20002;

        // 群内发言者 A 第 1 轮
        Assert.True(store.TryBeginTurn(groupA, user1, maxTurns: 2, ConversationTimeout, out GeminiTurnLease turn1));
        Assert.Equal(1, turn1.TurnNumber);
        Assert.Empty(turn1.History);
        // 并发请求被拒绝
        Assert.False(store.TryBeginTurn(groupA, user1, maxTurns: 2, ConversationTimeout, out _));
        // 同群不同发言者 B 互不干扰
        Assert.True(store.TryBeginTurn(groupA, user2, maxTurns: 2, ConversationTimeout, out GeminiTurnLease otherUser));
        store.AbortTurn(otherUser);

        store.CommitTurn(turn1, UserText("我叫小明"), ModelText("你好小明"));
        Assert.Equal(1, store.GetCompletedTurns(groupA, user1));

        // 失败的一轮不破坏已有历史与轮数
        Assert.True(store.TryBeginTurn(groupA, user1, maxTurns: 2, ConversationTimeout, out GeminiTurnLease failed));
        store.AbortTurn(failed);
        Assert.Equal(1, store.GetCompletedTurns(groupA, user1));

        // 群内发言者 A 第 2 轮（带第 1 轮历史）；达到上限后会话随即释放
        Assert.True(store.TryBeginTurn(groupA, user1, maxTurns: 2, ConversationTimeout, out GeminiTurnLease turn2));
        Assert.Equal(2, turn2.TurnNumber);
        Assert.Equal(2, turn2.History.Count);
        store.CommitTurn(turn2, UserText("我叫什么"), ModelText("你叫小明"));
        Assert.Equal(0, store.GetCompletedTurns(groupA, user1));

        // 第 3 次提问是新对话的第 1 轮，历史为空
        Assert.True(store.TryBeginTurn(groupA, user1, maxTurns: 2, ConversationTimeout, out GeminiTurnLease turn3));
        Assert.Equal(1, turn3.TurnNumber);
        Assert.Empty(turn3.History);
        store.CommitTurn(turn3, UserText("重新开始"), ModelText("好的"));
        Assert.Equal(1, store.GetCompletedTurns(groupA, user1));

        // 管理员调低上限后，已超出新上限的会话下一次提问同样重新开始
        Assert.True(store.TryBeginTurn(groupA, user1, maxTurns: 1, ConversationTimeout, out GeminiTurnLease lowered));
        Assert.True(lowered.ResetPrevious);
        Assert.Empty(lowered.History);
        store.AbortTurn(lowered);

        // 清空上下文
        store.Clear(groupA, user1);
        Assert.Equal(0, store.GetCompletedTurns(groupA, user1));
    }

    [Fact]
    public void ConversationStore_ExpiresIdleSessionsAndRefreshesOnEachReply()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        GeminiConversationStore store = new(time);
        GroupId group = 10002;
        UserId user = 20003;

        Assert.True(store.TryBeginTurn(group, user, maxTurns: 8, ConversationTimeout, out GeminiTurnLease turn1));
        store.CommitTurn(turn1, UserText("第一问"), ModelText("第一答"));

        // 闲置 29 分钟后继续提问仍带历史，成功回复后重新计时
        time.Advance(TimeSpan.FromMinutes(29));
        Assert.True(store.TryBeginTurn(group, user, maxTurns: 8, ConversationTimeout, out GeminiTurnLease turn2));
        Assert.Equal(2, turn2.TurnNumber);
        store.CommitTurn(turn2, UserText("第二问"), ModelText("第二答"));

        time.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(0, store.PurgeExpired(ConversationTimeout));
        Assert.Equal(2, store.GetCompletedTurns(group, user));

        // 自上一次回复起闲置满 30 分钟：下一次提问清空上下文，后台清理也会释放该会话
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(store.TryBeginTurn(group, user, maxTurns: 8, ConversationTimeout, out GeminiTurnLease expired));
        Assert.True(expired.ResetPrevious);
        Assert.Equal(1, expired.TurnNumber);
        Assert.Empty(expired.History);
        // 生成中的会话不被后台清理
        Assert.Equal(0, store.PurgeExpired(ConversationTimeout));
        store.AbortTurn(expired);
        Assert.Equal(1, store.PurgeExpired(ConversationTimeout));
        Assert.Equal(0, store.GetCompletedTurns(group, user));
    }

    [Fact]
    public void ConversationStore_TrimsEarliestMediaWhenExceedingBudget()
    {
        GeminiConversationStore store = new(new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")), maxSessionMediaBytes: 100);
        GroupId group = default;
        UserId user = 30001;

        Assert.True(store.TryBeginTurn(group, user, maxTurns: 8, ConversationTimeout, out GeminiTurnLease t1));
        store.CommitTurn(t1, UserImage("图1", bytes: 60), ModelText("收到图1"));
        Assert.Equal(60, store.GetMediaBytes(group, user));

        Assert.True(store.TryBeginTurn(group, user, maxTurns: 8, ConversationTimeout, out GeminiTurnLease t2));
        store.CommitTurn(t2, UserImage("图2", bytes: 60), ModelText("收到图2"));

        // 超出 100 字节上限后，最早一轮的 60 字节图片被裁剪，保留最新一轮的 60 字节图片。
        Assert.Equal(60, store.GetMediaBytes(group, user));
        Assert.True(store.TryBeginTurn(group, user, maxTurns: 8, ConversationTimeout, out GeminiTurnLease t3));
        Assert.Equal(GeminiConversationStore.TRIMMED_MEDIA_PLACEHOLDER, t3.History[0].Parts[1].Text);
        Assert.Null(t3.History[0].Parts[1].Media);
        Assert.NotNull(t3.History[2].Parts[1].Media);

        // 发送前按本轮图片占用的余量再裁剪历史：只剩 30 字节余量时最新一轮的历史图片也被替换，原快照不受影响。
        List<GeminiConversationMessage> fitted = GeminiConversationStore.TrimMedia(t3.History, maxBytes: 30);
        Assert.Equal(GeminiConversationStore.TRIMMED_MEDIA_PLACEHOLDER, fitted[2].Parts[1].Text);
        Assert.NotNull(t3.History[2].Parts[1].Media);
        store.AbortTurn(t3);
    }

    [Fact]
    public void CooldownTracker_StartsAfterReplyIsGlobalPerUserAndReleasesOnFailure()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        GeminiCooldownTracker tracker = new(time);
        UserId user = 40001;
        TimeSpan window = TimeSpan.FromSeconds(30);

        Assert.Equal(CooldownResult.Acquired, tracker.TryAcquire(user, window, out CooldownTicket generating, out _));
        // 生成中时其他会话的触发被拒绝，避免同时发起多个请求绕过冷却
        Assert.Equal(CooldownResult.Busy, tracker.TryAcquire(user, window, out _, out _));

        // 生成耗时 60 秒，冷却从回复发出后才开始计时
        time.Advance(TimeSpan.FromSeconds(60));
        tracker.Complete(generating);
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(CooldownResult.CoolingDown, tracker.TryAcquire(user, window, out _, out int remaining));
        Assert.Equal(20, remaining);

        // 冷却内重试不重置计时，再过 20 秒（共 30 秒）恢复可用
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(CooldownResult.Acquired, tracker.TryAcquire(user, window, out CooldownTicket failing, out _));

        // 调用失败释放占用且不开始冷却，可立即再次尝试
        tracker.Release(failing);
        Assert.Equal(CooldownResult.Acquired, tracker.TryAcquire(user, window, out CooldownTicket next, out _));
        tracker.Release(next);

        // 冷却为 0 时不限制，也不占用
        Assert.Equal(CooldownResult.Acquired, tracker.TryAcquire(user, TimeSpan.Zero, out CooldownTicket unlimited, out _));
        Assert.False(unlimited.Active);
        Assert.Equal(CooldownResult.Acquired, tracker.TryAcquire(user, TimeSpan.Zero, out _, out _));
    }

    [Fact]
    public void InputReader_ConvertsUnsupportedImageFormatsToPng()
    {
        // 1×1 透明 GIF：Gemini 不接受 GIF，取首帧转为 PNG。
        byte[] gif = Convert.FromHexString("47494638396101000100800000FFFFFF00000021F90401000000002C00000000010001000002024401003B");
        GeminiMediaPart? converted = GeminiInputReader.ToSupportedImage(gif, "image/gif");
        Assert.NotNull(converted);
        Assert.Equal("image/png", converted.MediaType);
        Assert.Equal(Convert.FromHexString("89504E470D0A1A0A"), converted.Data[..8]);

        // 支持的格式按内容识别并原样返回，不信任响应头。
        GeminiMediaPart? png = GeminiInputReader.ToSupportedImage(converted.Data, "application/octet-stream");
        Assert.Equal("image/png", png?.MediaType);
        Assert.Same(converted.Data, png?.Data);

        // SkiaSharp 无法识别时只信任声明为 HEIC/HEIF 的内容，其余无法识别的内容拒绝。
        byte[] unknown = [1, 2, 3, 4];
        Assert.Equal("image/heic", GeminiInputReader.ToSupportedImage(unknown, "image/HEIC")?.MediaType);
        Assert.Null(GeminiInputReader.ToSupportedImage(unknown, "image/gif"));
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

    private static GeminiConversationMessage UserImage(string text, int bytes) =>
        new()
        {
            Role = "user",
            Parts =
            [
                new GeminiConversationPart { Text = text },
                new GeminiConversationPart
                {
                    Media = new GeminiMediaPart
                    {
                        MediaType = "image/png",
                        Data      = new byte[bytes]
                    }
                }
            ]
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
