namespace Shinano.Infrastructure.ChatArchive;

/// <summary>归档缺口的原因，以 smallint 存储，取值不可更改。</summary>
public enum ArchiveGapCause : short
{
    /// <summary>数据库离线，消息无法入库。</summary>
    DatabaseOffline = 1,

    /// <summary>LLBot 断线，期间未上报的消息无法补回。</summary>
    BotDisconnected = 2,

    /// <summary>宿主归档缓冲已满，新记录被丢弃。</summary>
    Backlog = 3
}
