using System.Text.Json.Nodes;
using Npgsql;
using Shinano.Infrastructure.ChatArchive.Models;
using Shinano.Infrastructure.Hosting;
using Shinano.Tests.Infrastructure;

namespace Shinano.Tests.ChatArchive;

[Collection(InfrastructureCollection.NAME)]
public sealed class ChatArchiveStoreTests
{
    private const long SELF_ID = InfrastructureFixture.SELF_ID;

    private readonly InfrastructureFixture _fixture;

    public ChatArchiveStoreTests(InfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MarkInterrupted_TurnsPendingIntoMissing()
    {
        HostInfrastructure infrastructure = _fixture.Ready;
        ArchivedChatMessage message = Message(ChatArchiveFlags.MediaPending, DateTimeOffset.UtcNow);
        long? id = await infrastructure.ChatArchive.InsertAsync(SELF_ID, message, Token);
        Assert.NotNull(id);

        Assert.True(await infrastructure.ChatArchive.MarkInterruptedAsync(SELF_ID, Token) >= 1);

        Assert.Equal(
            (short)ChatArchiveFlags.ContentMissing,
            await ScalarAsync<short>(infrastructure, $"select archive_flags from {Schema}.chat_message where id = {id}"));
    }

    [Fact]
    public async Task Gaps_AreWritten_AndOldStatsAreDeleted()
    {
        HostInfrastructure infrastructure = _fixture.Ready;
        DateTimeOffset started = DateTimeOffset.UtcNow.AddMinutes(-5);
        await infrastructure.ChatArchive.WriteGapsAsync(
            SELF_ID,
            [
                new ArchiveGap
                {
                    Cause     = ArchiveGapCause.BotDisconnected,
                    StartedAt = started,
                    EndedAt   = started.AddMinutes(1)
                }
            ],
            Token);
        Assert.True(
            await ScalarAsync<long>(
                infrastructure,
                $"select count(*) from {Schema}.archive_gap where cause = {(short)ArchiveGapCause.BotDisconnected}") >= 1);

        ArchivedChatMessage old = Message(ChatArchiveFlags.None, DateTimeOffset.UtcNow);
        ArchivedChatMessage stale = new()
        {
            SentAt           = old.SentAt,
            StatDate         = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-400),
            ConversationType = old.ConversationType,
            ConversationId   = old.ConversationId,
            SenderId         = old.SenderId,
            MessageId        = old.MessageId,
            Text             = old.Text,
            Segments         = old.Segments,
            Flags            = old.Flags,
            Counts           = old.Counts
        };
        Assert.NotNull(await infrastructure.ChatArchive.InsertAsync(SELF_ID, stale, Token));

        Assert.True(
            await infrastructure.ChatArchive.DeleteStatsBeforeAsync(SELF_ID, stale.StatDate.AddDays(1), Token) >= 1);
        Assert.Equal(
            0,
            await ScalarAsync<long>(
                infrastructure,
                $"select count(*) from {Schema}.chat_stat_daily where conversation_id = {stale.ConversationId}"));
    }

    private string Schema => _fixture.Schemas.Account(SELF_ID);

    private static ArchivedChatMessage Message(ChatArchiveFlags flags, DateTimeOffset sentAt) =>
        new()
        {
            SentAt           = sentAt,
            StatDate         = DateOnly.FromDateTime(sentAt.UtcDateTime),
            ConversationType = ChatConversationType.Group,
            ConversationId   = Random.Shared.NextInt64(1_000_000, 9_000_000),
            SenderId         = 222,
            MessageId        = Random.Shared.NextInt64(1, long.MaxValue),
            Text             = null,
            Segments         = [new JsonObject { ["t"] = "img" }],
            Flags            = flags,
            Counts           = new ChatMessageCounts { ImageCount = 1 }
        };

    private static async Task<T> ScalarAsync<T>(HostInfrastructure infrastructure, string sql)
    {
        await using NpgsqlConnection connection = await infrastructure.Database.OpenConnectionAsync(Token);
        await using NpgsqlCommand command = new(sql, connection);
        object? value = await command.ExecuteScalarAsync(Token);
        return Assert.IsType<T>(value);
    }
}
