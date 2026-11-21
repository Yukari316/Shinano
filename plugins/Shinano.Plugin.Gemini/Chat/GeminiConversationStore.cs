using Shinano.Plugin.Gemini.Api;
using Sora.Core.Types;

namespace Shinano.Plugin.Gemini.Chat;

/// <summary>
/// 纯内存的 Gemini 对话会话存储：群聊按「群 + 发言者」独立，好友私聊按发言者独立。
/// 达到轮数上限，或上一轮回复后闲置超过对话超时，下一次提问即清空旧上下文并作为第 1 轮；同会话并发请求直接拒绝。
/// 历史图片与本轮图片合计受单次请求内联媒体上限约束，超出时从最早轮次起替换为文字占位。线程安全。
/// </summary>
public sealed class GeminiConversationStore
{
    /// <summary>历史媒体被裁剪后留下的文字占位。</summary>
    public const string TRIMMED_MEDIA_PLACEHOLDER = "[历史图片已省略]";

    private readonly Lock                                                  _sync     = new();
    private readonly Dictionary<(GroupId GroupId, UserId UserId), Session> _sessions = [];
    private readonly TimeProvider                                          _time;
    private readonly long                                                  _maxSessionMediaBytes;

    /// <summary>创建会话存储；单会话保留的媒体以单次请求的内联媒体上限为界，更早的图片不可能再发给模型。</summary>
    public GeminiConversationStore(TimeProvider time)
        : this(time, GeminiApiClient.MAX_INLINE_MEDIA_BYTES)
    {
    }

    /// <summary>创建指定单会话媒体上限的会话存储（供测试边界使用）。</summary>
    internal GeminiConversationStore(TimeProvider time, long maxSessionMediaBytes)
    {
        _time                 = time;
        _maxSessionMediaBytes = maxSessionMediaBytes;
    }

    /// <summary>
    /// 尝试开始一轮对话：若该发言者在当前会话已有生成中的请求则返回 false；
    /// 否则返回用于本次 API 请求的历史快照与本轮序号（超轮或闲置超时时从第 1 轮开始，但提交前不破坏旧历史）。
    /// </summary>
    public bool TryBeginTurn(GroupId groupId, UserId userId, int maxTurns, TimeSpan idleTimeout, out GeminiTurnLease lease)
    {
        int effectiveLimit = Math.Max(1, maxTurns);
        (GroupId GroupId, UserId UserId) key = (groupId, userId);
        DateTimeOffset now = _time.GetUtcNow();

        lock (_sync)
        {
            if (!_sessions.TryGetValue(key, out Session? session))
            {
                session = new Session();
                _sessions[key] = session;
            }

            if (session.InFlight)
            {
                lease = default;
                return false;
            }

            session.InFlight = true;
            bool willReset = session.CompletedTurns >= effectiveLimit || IsExpired(session, now, idleTimeout);
            IReadOnlyList<GeminiConversationMessage> history = willReset
                ? []
                : [.. session.Messages];

            lease = new GeminiTurnLease
            {
                GroupId       = groupId,
                UserId        = userId,
                ResetPrevious = willReset,
                TurnNumber    = willReset ? 1 : session.CompletedTurns + 1,
                TurnLimit     = effectiveLimit,
                History       = history
            };
            return true;
        }
    }

    /// <summary>
    /// 提交一轮成功完成的对话，追加用户与模型消息、重新开始闲置计时并执行媒体上限裁剪；
    /// 达到轮数上限时直接释放会话，因为下一次提问必然开启新对话。
    /// </summary>
    public void CommitTurn(
        GeminiTurnLease           lease,
        GeminiConversationMessage userMessage,
        GeminiConversationMessage modelMessage)
    {
        (GroupId GroupId, UserId UserId) key = (lease.GroupId, lease.UserId);
        DateTimeOffset now = _time.GetUtcNow();
        lock (_sync)
        {
            if (!_sessions.TryGetValue(key, out Session? session)) return;

            if (lease.ResetPrevious)
            {
                session.Messages.Clear();
                session.CompletedTurns = 0;
            }

            session.CompletedTurns++;
            if (session.CompletedTurns >= lease.TurnLimit)
            {
                _sessions.Remove(key);
                return;
            }

            session.Messages = TrimMedia([.. session.Messages, userMessage, modelMessage], _maxSessionMediaBytes);
            session.LastReplyAt = now;
            session.InFlight = false;
        }
    }

    /// <summary>取消一轮未成功的对话，释放并发锁且不修改历史或轮数；没有历史的会话直接释放。</summary>
    public void AbortTurn(GeminiTurnLease lease)
    {
        (GroupId GroupId, UserId UserId) key = (lease.GroupId, lease.UserId);
        lock (_sync)
        {
            if (!_sessions.TryGetValue(key, out Session? session)) return;
            session.InFlight = false;
            if (session.CompletedTurns == 0) _sessions.Remove(key);
        }
    }

    /// <summary>清空发言者在当前会话的对话上下文；若当前有生成中请求，仍可清空旧历史且保留并发状态标记。</summary>
    public void Clear(GroupId groupId, UserId userId)
    {
        (GroupId GroupId, UserId UserId) key = (groupId, userId);
        lock (_sync)
        {
            if (!_sessions.TryGetValue(key, out Session? session)) return;
            session.Messages.Clear();
            session.CompletedTurns = 0;
            if (!session.InFlight) _sessions.Remove(key);
        }
    }

    /// <summary>释放闲置超过 <paramref name="idleTimeout" /> 的会话，由插件的后台任务定期调用；生成中的会话不受影响。</summary>
    /// <returns>释放的会话数。</returns>
    public int PurgeExpired(TimeSpan idleTimeout)
    {
        DateTimeOffset now = _time.GetUtcNow();
        lock (_sync)
        {
            (GroupId GroupId, UserId UserId)[] expired =
            [
                .. _sessions.Where(pair => !pair.Value.InFlight && (pair.Value.CompletedTurns == 0 || IsExpired(pair.Value, now, idleTimeout)))
                            .Select(pair => pair.Key)
            ];
            foreach ((GroupId GroupId, UserId UserId) key in expired) _sessions.Remove(key);
            return expired.Length;
        }
    }

    /// <summary>
    /// 从最早的消息起把媒体替换为文字占位，直到媒体总字节不超过 <paramref name="maxBytes" />；返回新列表，不修改传入的消息。
    /// </summary>
    public static List<GeminiConversationMessage> TrimMedia(IReadOnlyList<GeminiConversationMessage> messages, long maxBytes)
    {
        long total = messages.Sum(MediaBytes);
        List<GeminiConversationMessage> result = new(messages.Count);
        foreach (GeminiConversationMessage message in messages)
        {
            if (total <= maxBytes || MediaBytes(message) == 0)
            {
                result.Add(message);
                continue;
            }

            List<GeminiConversationPart> parts = new(message.Parts.Count);
            foreach (GeminiConversationPart part in message.Parts)
            {
                if (total > maxBytes && part.Media is { } media)
                {
                    total -= media.Data.Length;
                    parts.Add(
                        new GeminiConversationPart
                        {
                            Text             = TRIMMED_MEDIA_PLACEHOLDER,
                            ThoughtSignature = part.ThoughtSignature
                        });
                    continue;
                }

                parts.Add(part);
            }

            result.Add(
                new GeminiConversationMessage
                {
                    Role  = message.Role,
                    Parts = parts
                });
        }

        return result;
    }

    /// <summary>查询当前已完成轮数（供测试核对）。</summary>
    internal int GetCompletedTurns(GroupId groupId, UserId userId)
    {
        lock (_sync)
        {
            return _sessions.TryGetValue((groupId, userId), out Session? session) ? session.CompletedTurns : 0;
        }
    }

    /// <summary>查询当前会话缓存的媒体总字节数（供测试核对）。</summary>
    internal long GetMediaBytes(GroupId groupId, UserId userId)
    {
        lock (_sync)
        {
            return _sessions.TryGetValue((groupId, userId), out Session? session) ? session.Messages.Sum(MediaBytes) : 0;
        }
    }

    private static bool IsExpired(Session session, DateTimeOffset now, TimeSpan idleTimeout) =>
        session.CompletedTurns > 0 && now - session.LastReplyAt >= idleTimeout;

    private static long MediaBytes(GeminiConversationMessage message) =>
        message.Parts.Sum(part => (long)(part.Media?.Data.Length ?? 0));

    private sealed class Session
    {
        public List<GeminiConversationMessage> Messages       { get; set; } = [];
        public int                             CompletedTurns { get; set; }
        public DateTimeOffset                  LastReplyAt    { get; set; }
        public bool                            InFlight       { get; set; }
    }
}

/// <summary>一次已占位的对话轮次租约。</summary>
public readonly struct GeminiTurnLease
{
    /// <summary>群号；私聊为 default。</summary>
    public GroupId GroupId { get; init; }

    /// <summary>发言者。</summary>
    public UserId UserId { get; init; }

    /// <summary>本次是否因超轮或闲置超时而重置了之前的上下文。</summary>
    public bool ResetPrevious { get; init; }

    /// <summary>本次若成功，对应的轮数序号（从 1 开始）。</summary>
    public int TurnNumber { get; init; }

    /// <summary>本次对话适用的轮数上限。</summary>
    public int TurnLimit { get; init; }

    /// <summary>发送给模型的历史消息快照。</summary>
    public IReadOnlyList<GeminiConversationMessage> History { get; init; }
}
