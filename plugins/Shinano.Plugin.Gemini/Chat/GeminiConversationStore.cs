using Shinano.Plugin.Gemini.Api;
using Sora.Core.Types;

namespace Shinano.Plugin.Gemini.Chat;

/// <summary>
/// 纯内存的 Gemini 对话会话存储：群聊按「群 + 发言者」独立，好友私聊按发言者独立。
/// 达到轮数上限后的下一次提问清空旧上下文并作为第 1 轮；同会话并发请求直接拒绝；
/// 单会话内存媒体总占用上限 50 MB，超限时裁剪最早轮次的媒体字节仅保留文字占位。线程安全。
/// </summary>
public sealed class GeminiConversationStore
{
    /// <summary>单个会话内存中保留的历史媒体字节上限（50 MB）。</summary>
    public const long MAX_SESSION_MEDIA_BYTES = 50L * 1024 * 1024;

    /// <summary>历史媒体被裁剪后留下的文字占位。</summary>
    public const string TRIMMED_MEDIA_PLACEHOLDER = "[历史图片已因内存上限省略]";

    private readonly Lock                                               _sync     = new();
    private readonly Dictionary<(GroupId GroupId, UserId UserId), Session> _sessions = [];
    private readonly long                                               _maxSessionMediaBytes;

    /// <summary>创建使用默认 50 MB 媒体上限的会话存储。</summary>
    public GeminiConversationStore()
        : this(MAX_SESSION_MEDIA_BYTES)
    {
    }

    /// <summary>创建指定单会话媒体上限的会话存储（供测试边界使用）。</summary>
    internal GeminiConversationStore(long maxSessionMediaBytes) => _maxSessionMediaBytes = maxSessionMediaBytes;

    /// <summary>
    /// 尝试开始一轮对话：若该发言者在当前会话已有生成中的请求则返回 false；
    /// 否则返回用于本次 API 请求的历史快照与本轮序号（超限时自动从第 1 轮开始，但提交前不破坏旧历史）。
    /// </summary>
    public bool TryBeginTurn(GroupId groupId, UserId userId, int maxTurns, out GeminiTurnLease lease)
    {
        int effectiveLimit = Math.Max(1, maxTurns);
        (GroupId GroupId, UserId UserId) key = (groupId, userId);

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
            bool willReset = session.CompletedTurns >= effectiveLimit;
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

    /// <summary>提交一轮成功完成的对话，追加用户与模型消息并执行 50 MB 媒体上限裁剪。</summary>
    public void CommitTurn(
        GeminiTurnLease           lease,
        GeminiConversationMessage userMessage,
        GeminiConversationMessage modelMessage)
    {
        (GroupId GroupId, UserId UserId) key = (lease.GroupId, lease.UserId);
        lock (_sync)
        {
            if (!_sessions.TryGetValue(key, out Session? session))
            {
                session = new Session();
                _sessions[key] = session;
            }

            if (lease.ResetPrevious)
            {
                session.Messages.Clear();
                session.CompletedTurns = 0;
            }

            session.Messages.Add(userMessage);
            session.Messages.Add(modelMessage);
            session.CompletedTurns++;
            TrimSessionMedia(session);
            session.InFlight = false;
        }
    }

    /// <summary>取消一轮未成功的对话，释放并发锁且不修改历史或轮数。</summary>
    public void AbortTurn(GeminiTurnLease lease)
    {
        (GroupId GroupId, UserId UserId) key = (lease.GroupId, lease.UserId);
        lock (_sync)
        {
            if (_sessions.TryGetValue(key, out Session? session))
                session.InFlight = false;
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
            return _sessions.TryGetValue((groupId, userId), out Session? session) ? TotalMediaBytes(session) : 0;
        }
    }

    private void TrimSessionMedia(Session session)
    {
        long total = TotalMediaBytes(session);
        if (total <= _maxSessionMediaBytes) return;

        for (int messageIndex = 0; messageIndex < session.Messages.Count && total > _maxSessionMediaBytes; messageIndex++)
        {
            GeminiConversationMessage message = session.Messages[messageIndex];
            bool modified = false;
            List<GeminiConversationPart> updated = new(message.Parts.Count);

            foreach (GeminiConversationPart part in message.Parts)
            {
                if (total > _maxSessionMediaBytes && part.Media is { } media)
                {
                    total -= media.Data.Length;
                    modified = true;
                    updated.Add(
                        new GeminiConversationPart
                        {
                            Text             = TRIMMED_MEDIA_PLACEHOLDER,
                            ThoughtSignature = part.ThoughtSignature
                        });
                }
                else
                {
                    updated.Add(part);
                }
            }

            if (modified)
            {
                session.Messages[messageIndex] = new GeminiConversationMessage
                {
                    Role  = message.Role,
                    Parts = updated
                };
            }
        }
    }

    private static long TotalMediaBytes(Session session) =>
        session.Messages.Sum(message => message.Parts.Sum(part => (long)(part.Media?.Data.Length ?? 0)));

    private sealed class Session
    {
        public List<GeminiConversationMessage> Messages       { get; } = [];
        public int                             CompletedTurns { get; set; }
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

    /// <summary>本次是否因超轮而重置了之前的上下文。</summary>
    public bool ResetPrevious { get; init; }

    /// <summary>本次若成功，对应的轮数序号（从 1 开始）。</summary>
    public int TurnNumber { get; init; }

    /// <summary>本次对话适用的轮数上限。</summary>
    public int TurnLimit { get; init; }

    /// <summary>发送给模型的历史消息快照。</summary>
    public IReadOnlyList<GeminiConversationMessage> History { get; init; }
}
