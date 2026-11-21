using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Shinano.Plugin.Gemini.Api;

/// <summary>
/// 原生 Google Gemini REST API 客户端：支持拉取可用模型列表、多模态多轮 <c>generateContent</c>、
/// <c>thinkingConfig</c>、<c>thoughtSignature</c> 透传、Google Search Grounding 与可选 HTTP 代理。
/// API Key 一律经 <c>x-goog-api-key</c> 请求头发送，不写入 URL 或日志。
/// </summary>
public sealed class GeminiApiClient : IDisposable
{
    private const string API_KEY_HEADER    = "x-goog-api-key";
    private const string API_VERSION_PATH  = "v1beta";
    private const string GENERATE_METHOD   = "generateContent";
    private const int    MODELS_PAGE_SIZE  = 200;
    private const int    MAX_MODEL_PAGES   = 10;
    private const int    LIST_TIMEOUT_SEC  = 30;

    private readonly ILogger<GeminiApiClient>              _logger;
    private readonly Func<string, HttpMessageHandler>?     _handlerFactory;
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new(StringComparer.Ordinal);

    /// <summary>创建默认客户端。</summary>
    public GeminiApiClient(ILogger<GeminiApiClient> logger)
        : this(logger, null)
    {
    }

    /// <summary>创建可注入自定义消息处理器的客户端（供单元测试使用）。</summary>
    internal GeminiApiClient(ILogger<GeminiApiClient> logger, Func<string, HttpMessageHandler>? handlerFactory)
    {
        _logger         = logger;
        _handlerFactory = handlerFactory;
    }

    /// <summary>拉取支持 <c>generateContent</c> 的可用模型列表。</summary>
    public async Task<IReadOnlyList<GeminiModelInfo>> ListModelsAsync(
        string            baseUrl,
        string            proxyUrl,
        string            apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new GeminiApiException("尚未配置 Gemini API Key");

        string root = NormalizeBaseUrl(baseUrl);
        HttpClient client = GetClient(proxyUrl);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(LIST_TIMEOUT_SEC));

        List<GeminiModelInfo> models = [];
        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);
        string? pageToken = null;

        for (int page = 0; page < MAX_MODEL_PAGES; page++)
        {
            string url = $"{root}/{API_VERSION_PATH}/models?pageSize={MODELS_PAGE_SIZE}";
            if (!string.IsNullOrEmpty(pageToken))
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            using HttpRequestMessage message = new(HttpMethod.Get, url);
            message.Headers.TryAddWithoutValidation(API_KEY_HEADER, apiKey.Trim());

            using HttpResponseMessage response = await SendAsync(client, message, timeout.Token);
            string body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw CreateHttpException(response.StatusCode, body);

            using JsonDocument document = ParseJson(body);
            JsonElement rootElement = document.RootElement;
            if (rootElement.TryGetProperty("models", out JsonElement listElement) && listElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in listElement.EnumerateArray())
                {
                    if (TryParseModel(item) is not { } model || !seenIds.Add(model.Id)) continue;
                    models.Add(model);
                }
            }

            if (!rootElement.TryGetProperty("nextPageToken", out JsonElement tokenElement)
                || tokenElement.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(tokenElement.GetString()))
                break;

            pageToken = tokenElement.GetString();
        }

        models.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase));
        return models;
    }

    /// <summary>调用 Gemini <c>generateContent</c> 生成回复。</summary>
    public async Task<GeminiGenerateResponse> GenerateAsync(GeminiGenerateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            throw new GeminiApiException("尚未配置 Gemini API Key");

        string modelId = GeminiModelCapabilities.NormalizeModelId(request.Model);
        if (modelId.Length == 0)
            throw new GeminiApiException("未配置 Gemini 模型");

        string root = NormalizeBaseUrl(request.BaseUrl);
        string url = $"{root}/{API_VERSION_PATH}/models/{Uri.EscapeDataString(modelId)}:generateContent";
        JsonObject payload = BuildGeneratePayload(request, modelId);

        HttpClient client = GetClient(request.ProxyUrl);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 10, 600)));

        using HttpRequestMessage message = new(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        message.Headers.TryAddWithoutValidation(API_KEY_HEADER, request.ApiKey.Trim());

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        using HttpResponseMessage response = await SendAsync(client, message, timeout.Token);
        TimeSpan responseStartElapsed = DateTimeOffset.UtcNow - startedAt;
        _logger.LogInformation(
            "Gemini API 开始响应：model={Model}，HTTP {StatusCode}，等待 {FirstResponseMs} ms",
            modelId,
            (int)response.StatusCode,
            (long)responseStartElapsed.TotalMilliseconds);

        string body = await response.Content.ReadAsStringAsync(timeout.Token);
        TimeSpan elapsed = DateTimeOffset.UtcNow - startedAt;

        if (!response.IsSuccessStatusCode)
            throw CreateHttpException(response.StatusCode, body);

        return ParseGenerateResponse(body, request.MaxSearchSources, elapsed);
    }

    /// <summary>构造 <c>generateContent</c> 请求体 JSON（独立公开便于单元测试断言）。</summary>
    internal static JsonObject BuildGeneratePayload(GeminiGenerateRequest request, string modelId)
    {
        JsonArray contents = [];
        foreach (GeminiConversationMessage turn in request.Messages)
        {
            JsonArray parts = [];
            foreach (GeminiConversationPart part in turn.Parts)
            {
                JsonObject partObject = [];
                if (!string.IsNullOrEmpty(part.Text))
                    partObject["text"] = part.Text;
                if (part.Media is { } media)
                {
                    partObject["inlineData"] = new JsonObject
                    {
                        ["mimeType"] = media.MediaType,
                        ["data"]     = Convert.ToBase64String(media.Data)
                    };
                }

                if (!string.IsNullOrEmpty(part.ThoughtSignature))
                    partObject["thoughtSignature"] = part.ThoughtSignature;

                if (partObject.Count > 0) parts.Add(partObject);
            }

            if (parts.Count == 0) continue;
            contents.Add(
                new JsonObject
                {
                    ["role"]  = turn.Role,
                    ["parts"] = parts
                });
        }

        JsonObject payload = new()
        {
            ["contents"] = contents
        };

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            payload["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["text"] = request.SystemPrompt.Trim()
                    }
                }
            };
        }

        if (GeminiModelCapabilities.SupportsThinking(modelId))
        {
            JsonObject thinkingConfig = [];
            string normalizedEffort = GeminiModelCapabilities.NormalizeEffort(modelId, request.Effort);
            if (GeminiModelCapabilities.IsExplicitThinkingLevel(normalizedEffort))
                thinkingConfig["thinkingLevel"] = normalizedEffort;
            if (request.IncludeThoughts)
                thinkingConfig["includeThoughts"] = true;

            if (thinkingConfig.Count > 0)
            {
                payload["generationConfig"] = new JsonObject
                {
                    ["thinkingConfig"] = thinkingConfig
                };
            }
        }

        if (request.EnableGoogleSearch)
        {
            payload["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["google_search"] = new JsonObject()
                }
            };
        }

        return payload;
    }

    /// <summary>解析 <c>generateContent</c> 响应体 JSON（独立公开便于单元测试断言）。</summary>
    internal static GeminiGenerateResponse ParseGenerateResponse(string responseJson, int maxSearchSources, TimeSpan elapsed = default)
    {
        using JsonDocument document = ParseJson(responseJson);
        JsonElement root = document.RootElement;
        GeminiUsageMetadata? usage = ParseUsage(root);

        if (!root.TryGetProperty("candidates", out JsonElement candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            string blockReason = TryReadPromptBlockReason(root);
            if (blockReason.Length > 0)
                throw new GeminiApiException($"请求被模型安全策略拦截（{blockReason}）");
            return new GeminiGenerateResponse
            {
                AnswerText = string.Empty,
                ModelParts = [],
                Usage      = usage,
                Elapsed    = elapsed
            };
        }

        JsonElement first = candidates[0];
        List<string> answerSegments = [];
        List<string> thoughtSegments = [];
        List<GeminiConversationPart> modelParts = [];
        string? fallbackThoughtSignature = null;

        if (first.TryGetProperty("content", out JsonElement content)
            && content.TryGetProperty("parts", out JsonElement parts)
            && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement part in parts.EnumerateArray())
            {
                bool isThought = part.TryGetProperty("thought", out JsonElement thoughtFlag)
                                 && thoughtFlag.ValueKind == JsonValueKind.True;
                string? text = part.TryGetProperty("text", out JsonElement textElement) && textElement.ValueKind == JsonValueKind.String
                    ? textElement.GetString()
                    : null;
                string? signature = part.TryGetProperty("thoughtSignature", out JsonElement sigElement) && sigElement.ValueKind == JsonValueKind.String
                    ? sigElement.GetString()
                    : null;

                if (isThought)
                {
                    if (!string.IsNullOrWhiteSpace(text)) thoughtSegments.Add(text.Trim());
                    if (!string.IsNullOrEmpty(signature)) fallbackThoughtSignature = signature;
                    continue;
                }

                if (!string.IsNullOrEmpty(text)) answerSegments.Add(text);
                if (!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(signature))
                {
                    modelParts.Add(
                        new GeminiConversationPart
                        {
                            Text             = text,
                            ThoughtSignature = signature
                        });
                }
            }
        }

        // 如果签名仅附在思考段而正式回答段未携带，补附到最后一条正式段上，保证多轮不丢失签名。
        if (!string.IsNullOrEmpty(fallbackThoughtSignature)
            && modelParts.Count > 0
            && modelParts.All(part => string.IsNullOrEmpty(part.ThoughtSignature)))
        {
            GeminiConversationPart last = modelParts[^1];
            modelParts[^1] = new GeminiConversationPart
            {
                Text             = last.Text,
                Media            = last.Media,
                ThoughtSignature = fallbackThoughtSignature
            };
        }

        IReadOnlyList<GeminiSearchSource> sources = ParseSources(first, Math.Clamp(maxSearchSources, 1, 20));
        string answerText = string.Concat(answerSegments).Trim();

        if (answerText.Length == 0
            && first.TryGetProperty("finishReason", out JsonElement finishReason)
            && finishReason.ValueKind == JsonValueKind.String
            && finishReason.GetString() is { } reason
            && !string.Equals(reason, "STOP", StringComparison.OrdinalIgnoreCase))
        {
            throw new GeminiApiException($"模型提前终止输出（{reason}）");
        }

        return new GeminiGenerateResponse
        {
            AnswerText     = answerText,
            ThoughtSummary = string.Join("\n\n", thoughtSegments),
            ModelParts     = modelParts,
            Sources        = sources,
            Usage          = usage,
            Elapsed        = elapsed
        };
    }

    private static GeminiUsageMetadata? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
            return null;

        int? prompt = TryReadInt(usage, "promptTokenCount");
        int? cached = TryReadInt(usage, "cachedContentTokenCount");
        int? candidates = TryReadInt(usage, "candidatesTokenCount");
        int? thoughts = TryReadInt(usage, "thoughtsTokenCount");
        int? total = TryReadInt(usage, "totalTokenCount");
        if (!prompt.HasValue && !cached.HasValue && !candidates.HasValue && !thoughts.HasValue && !total.HasValue)
            return null;

        return new GeminiUsageMetadata
        {
            PromptTokens     = prompt,
            CachedTokens     = cached,
            CandidatesTokens = candidates,
            ThoughtsTokens   = thoughts,
            TotalTokens      = total
        };
    }

    private static int? TryReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int parsed)
            ? parsed
            : null;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (HttpClient client in _clients.Values) client.Dispose();
        _clients.Clear();
    }

    private HttpClient GetClient(string? proxyUrl)
    {
        string key = proxyUrl?.Trim() ?? string.Empty;
        return _clients.GetOrAdd(
            key,
            static (proxy, factory) =>
            {
                HttpMessageHandler handler = factory is not null
                    ? factory(proxy)
                    : CreateHandler(proxy);
                return new HttpClient(handler, disposeHandler: true)
                {
                    // 单次调用通过 CancellationTokenSource 控制精确超时。
                    Timeout = Timeout.InfiniteTimeSpan
                };
            },
            _handlerFactory);
    }

    private static SocketsHttpHandler CreateHandler(string proxyUrl)
    {
        SocketsHttpHandler handler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression   = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        if (proxyUrl.Length > 0)
        {
            if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out Uri? proxyUri))
                throw new GeminiApiException($"代理地址格式无效：{proxyUrl}");

            handler.UseProxy = true;
            handler.Proxy = new WebProxy(proxyUri);
        }

        return handler;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new GeminiApiException($"网络请求失败：{exception.Message}", null, exception);
        }
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        string trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/" + API_VERSION_PATH, StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^(API_VERSION_PATH.Length + 1)];
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new GeminiApiException($"API Base URL 格式无效：{baseUrl}");

        return trimmed;
    }

    private static JsonDocument ParseJson(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new GeminiApiException("上游返回了无法解析的 JSON 响应", null, exception);
        }
    }

    private static GeminiApiException CreateHttpException(HttpStatusCode statusCode, string body)
    {
        int code = (int)statusCode;
        string detail = TryReadErrorMessage(body);
        string message = detail.Length > 0
            ? $"HTTP {code}：{detail}"
            : $"HTTP {code}（{statusCode}）";
        return new GeminiApiException(message, code);
    }

    private static string TryReadErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out JsonElement error)
                && error.TryGetProperty("message", out JsonElement msg)
                && msg.ValueKind == JsonValueKind.String)
                return msg.GetString()?.Trim() ?? string.Empty;
        }
        catch (JsonException)
        {
            // 非 JSON 错误体直接截断返回。
        }

        string plain = body.Trim();
        return plain.Length > 200 ? plain[..200] + "…" : plain;
    }

    private static string TryReadPromptBlockReason(JsonElement root)
    {
        if (root.TryGetProperty("promptFeedback", out JsonElement feedback)
            && feedback.TryGetProperty("blockReason", out JsonElement reason)
            && reason.ValueKind == JsonValueKind.String)
            return reason.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static GeminiModelInfo? TryParseModel(JsonElement element)
    {
        if (!element.TryGetProperty("name", out JsonElement nameElement) || nameElement.ValueKind != JsonValueKind.String)
            return null;

        string id = GeminiModelCapabilities.NormalizeModelId(nameElement.GetString());
        if (id.Length == 0) return null;

        if (element.TryGetProperty("supportedGenerationMethods", out JsonElement methods) && methods.ValueKind == JsonValueKind.Array)
        {
            bool supportsGenerate = methods.EnumerateArray()
                                           .Any(method => method.ValueKind == JsonValueKind.String
                                                          && string.Equals(method.GetString(), GENERATE_METHOD, StringComparison.OrdinalIgnoreCase));
            if (!supportsGenerate) return null;
        }

        string displayName = element.TryGetProperty("displayName", out JsonElement displayElement) && displayElement.ValueKind == JsonValueKind.String
            ? displayElement.GetString() ?? id
            : id;
        string description = element.TryGetProperty("description", out JsonElement descElement) && descElement.ValueKind == JsonValueKind.String
            ? descElement.GetString() ?? string.Empty
            : string.Empty;
        bool? thinking = element.TryGetProperty("thinking", out JsonElement thinkingElement)
                         && thinkingElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? thinkingElement.GetBoolean()
            : null;

        return new GeminiModelInfo
        {
            Id             = id,
            DisplayName    = displayName,
            Description    = description,
            Thinking       = thinking,
            AllowedEfforts = GeminiModelCapabilities.GetAllowedEfforts(id, thinking)
        };
    }

    private static IReadOnlyList<GeminiSearchSource> ParseSources(JsonElement candidate, int maxSources)
    {
        if (!candidate.TryGetProperty("groundingMetadata", out JsonElement metadata)
            || !metadata.TryGetProperty("groundingChunks", out JsonElement chunks)
            || chunks.ValueKind != JsonValueKind.Array)
            return [];

        List<GeminiSearchSource> sources = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (JsonElement chunk in chunks.EnumerateArray())
        {
            if (sources.Count >= maxSources) break;
            if (!chunk.TryGetProperty("web", out JsonElement web) || web.ValueKind != JsonValueKind.Object) continue;

            string uri = web.TryGetProperty("uri", out JsonElement uriElement) && uriElement.ValueKind == JsonValueKind.String
                ? uriElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            string title = web.TryGetProperty("title", out JsonElement titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;

            string domain = ExtractDomain(uri, title);
            if (title.Length == 0 && domain.Length == 0) continue;

            string displayTitle = title.Length > 0 ? title : domain;
            string dedupeKey = uri.Length > 0 ? uri : $"{displayTitle}|{domain}";
            if (!seen.Add(dedupeKey)) continue;

            sources.Add(
                new GeminiSearchSource
                {
                    Title  = displayTitle,
                    Domain = domain,
                    Uri    = uri
                });
        }

        return sources;
    }

    private static string ExtractDomain(string uri, string title)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed)
            && !string.IsNullOrEmpty(parsed.Host)
            && !parsed.Host.Equals("vertexaisearch.cloud.google.com", StringComparison.OrdinalIgnoreCase))
            return parsed.Host;

        // Google Grounding 跳转链接常将真实域名直接写在 title 中。
        if (title.Contains('.', StringComparison.Ordinal) && !title.Contains(' ', StringComparison.Ordinal))
            return title;

        return string.Empty;
    }
}
