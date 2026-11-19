using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Shinano.Framework.Plugins;
using Shinano.Infrastructure.Hosting;
using Sora.Entities.Interfaces;

namespace Shinano.Host.Bot;

/// <summary>当前机器人连接，由 <see cref="BotService" /> 在连接与断线时更新。</summary>
public sealed class BotAccessor : IBotAccessor
{
    private const string JSON_MEDIA_TYPE = "application/json";

    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly string     _apiBase;
    private volatile IBotApi?   _api;
    private long                _selfId;

    /// <summary>创建访问器。</summary>
    /// <param name="llBot">LLBot 连接配置，用于 <see cref="CallMilkyAsync" />。</param>
    /// <param name="deployment">部署配置，提供 Super User 列表。</param>
    public BotAccessor(LlBotOptions llBot, DeploymentOptions deployment)
    {
        SuperUsers = [.. deployment.SuperUsers];
        string prefix = string.IsNullOrEmpty(llBot.Prefix) ? string.Empty : "/" + llBot.Prefix.Trim('/');
        _apiBase = $"http://{llBot.Host}:{llBot.Port}{prefix}/api/";
        _http    = new HttpClient { Timeout = ApiTimeout };
        if (!string.IsNullOrEmpty(llBot.AccessToken))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", llBot.AccessToken);
    }

    /// <inheritdoc />
    public IBotApi? Api => _api;

    /// <inheritdoc />
    public long SelfId => Interlocked.Read(ref _selfId);

    /// <inheritdoc />
    public IReadOnlyList<long> SuperUsers { get; }

    /// <inheritdoc />
    public async Task<MilkyCallResult> CallMilkyAsync(string action, JsonObject parameters, CancellationToken cancellationToken)
    {
        using StringContent content = new(parameters.ToJsonString(), Encoding.UTF8, JSON_MEDIA_TYPE);
        using HttpResponseMessage response = await _http.PostAsync(_apiBase + Uri.EscapeDataString(action), content, cancellationToken);
        return new MilkyCallResult
        {
            StatusCode = (int)response.StatusCode,
            Body       = await response.Content.ReadAsStringAsync(cancellationToken)
        };
    }

    /// <summary>记录已连接的账号与 API。</summary>
    internal void SetConnected(IBotApi api, long selfId)
    {
        Interlocked.Exchange(ref _selfId, selfId);
        _api = api;
    }

    /// <summary>记录断线；保留账号以便断线期间仍能访问账号数据。</summary>
    internal void SetDisconnected() => _api = null;
}
