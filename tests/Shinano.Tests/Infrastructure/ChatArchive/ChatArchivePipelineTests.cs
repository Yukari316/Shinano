using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shinano.Infrastructure.ChatArchive;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Media;
using Shinano.Tests.Support;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Tests.Infrastructure.ChatArchive;

[Collection(InfrastructureCollection.NAME)]
public sealed class ChatArchivePipelineTests
{
    private const long MEMBER_ID   = 222;
    private const long OUTBOUND_ID = InfrastructureFixture.SELF_ID;

    private static readonly TimeSpan ArchiveTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval   = TimeSpan.FromMilliseconds(100);

    private readonly InfrastructureFixture _fixture;

    public ChatArchivePipelineTests(InfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pipeline_ArchivesInboundAndOutbound_WithMediaForwardsDedupAndStats()
    {
        HostInfrastructure infrastructure = _fixture.Ready;
        long groupId = Random.Shared.NextInt64(1_000_000, 9_000_000);
        await using ArchiveHarness harness = await ArchiveHarness.StartAsync(infrastructure);
        FakeMilkyServer server = harness.Server;
        byte[] sharedImage = Png();
        string imageUrl = server.RegisterMedia("a.png", sharedImage);
        string faceUrl  = server.RegisterMedia("b.png", Png());
        string innerUrl = server.RegisterMedia("c.png", Png());
        server.RegisterForward(
            "fw1",
            [
                new JsonObject
                {
                    ["message_seq"] = 1,
                    ["sender_name"] = "转发者",
                    ["avatar_url"]  = "",
                    ["time"]        = 1_700_000_000,
                    ["segments"]    = new JsonArray(Text("不计入统计"), Image(innerUrl))
                }
            ]);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        long firstSeq = server.NextMessageSeq();
        JsonArray first = [Text("你好 👍"), Image(imageUrl), MarketFace(faceUrl), Mention(42)];
        await server.PushGroupMessageAsync(groupId, MEMBER_ID, "member", first, firstSeq, now);
        await server.PushGroupMessageAsync(groupId, MEMBER_ID, "member", (JsonArray)first.DeepClone(), firstSeq, now);
        long forwardSeq = server.NextMessageSeq();
        await server.PushGroupMessageAsync(groupId, MEMBER_ID, "member", [Forward("fw1")], forwardSeq, now);
        long brokenSeq = server.NextMessageSeq();
        await server.PushGroupMessageAsync(
            groupId,
            MEMBER_ID,
            "member",
            [Image($"http://127.0.0.1:{server.Port}/media/missing.png")],
            brokenSeq,
            now);
        await harness.Api.SendGroupMessageAsync(
            groupId,
            SegmentBuilder.Text("回复") + SegmentBuilder.Image("base64://" + Convert.ToBase64String(sharedImage), default),
            Token);

        List<ArchivedRow> rows = await WaitForArchivedAsync(infrastructure, groupId, expectedRows: 4);

        ArchivedRow firstRow = rows.Single(row => row.MessageId == firstSeq);
        Assert.Equal("你好 👍", firstRow.Text);
        Assert.Equal(ChatArchiveFlags.None, firstRow.Flags);
        JsonArray firstSegments = firstRow.Segments();
        long imageId = MediaId(firstSegments[1]);
        Assert.Equal("mface", firstSegments[2]?["t"]?.GetValue<string>());
        Assert.NotEqual(imageId, MediaId(firstSegments[2]));
        Assert.Equal(42, firstSegments[3]?["u"]?.GetValue<long>());

        ArchivedRow forwardRow = rows.Single(row => row.MessageId == forwardSeq);
        Assert.Null(forwardRow.Text);
        Assert.Equal(ChatArchiveFlags.None, forwardRow.Flags);
        JsonNode? innerMessage = forwardRow.Segments()[0]?["msgs"]?[0];
        Assert.Equal("转发者", innerMessage?["n"]?.GetValue<string>());
        MediaId(innerMessage?["s"]?[1]);

        ArchivedRow brokenRow = rows.Single(row => row.MessageId == brokenSeq);
        Assert.Equal(ChatArchiveFlags.ContentMissing, brokenRow.Flags);
        Assert.Null(brokenRow.Segments()[0]?["m"]);

        ArchivedRow outbound = rows.Single(row => row.SenderId == OUTBOUND_ID);
        Assert.Equal("回复", outbound.Text);
        Assert.Equal(ChatArchiveFlags.None, outbound.Flags);
        // 发送的图片与收到的第一张图片内容相同，复用同一个聊天媒体。
        Assert.Equal(imageId, MediaId(outbound.Segments()[1]));

        Dictionary<long, StatRow> stats = await ReadStatsAsync(infrastructure, groupId);
        Assert.Equal(3, stats[MEMBER_ID].MessageCount);
        Assert.Equal(3, stats[MEMBER_ID].CharCount);
        Assert.Equal(3, stats[MEMBER_ID].ImageCount);
        Assert.Equal(1, stats[OUTBOUND_ID].MessageCount);
        Assert.Equal(2, stats[OUTBOUND_ID].CharCount);
        Assert.Equal(1, stats[OUTBOUND_ID].ImageCount);
    }

    [Fact]
    public async Task MediaArchiver_RequestTimeout_MarksMediaMissingInsteadOfCancelling()
    {
        HostInfrastructure infrastructure = _fixture.Ready;
        // 只监听不接受连接：请求一直等不到响应，由 HttpClient 自身超时抛出 TaskCanceledException。
        using TcpListener stalled = new(IPAddress.Loopback, 0);
        stalled.Start();
        using HttpClient http = new() { Timeout = TimeSpan.FromMilliseconds(200) };
        ChatMediaArchiver archiver = new(
            infrastructure.ChatMedia,
            new MediaDownloader(http, new MediaOptions()),
            infrastructure.StorageAvailability,
            NullLogger<ChatMediaArchiver>.Instance);
        ChatSegmentEncoder encoder = new();
        int port = ((IPEndPoint)stalled.LocalEndpoint).Port;
        JsonArray segments = encoder.Encode([new ImageSegment { FileUri = $"http://127.0.0.1:{port}/a.png" }], depth: 0);
        ChatArchiveEntry entry = new()
        {
            SelfId     = InfrastructureFixture.SELF_ID,
            IsOutbound = false,
            Encoder    = encoder,
            Message = new ArchivedChatMessage
            {
                SentAt           = DateTimeOffset.UtcNow,
                StatDate         = DateOnly.FromDateTime(DateTime.UtcNow),
                ConversationType = ChatConversationType.Group,
                ConversationId   = 1,
                SenderId         = MEMBER_ID,
                MessageId        = 1,
                Text             = null,
                Segments         = segments,
                Flags            = ChatArchiveFlags.MediaPending,
                Counts           = new ChatMessageCounts()
            }
        };

        ChatArchiveFlags flags = await archiver.ArchiveAsync(entry, api: null, Token);

        Assert.True(flags.HasFlag(ChatArchiveFlags.ContentMissing));
    }

    private async Task<List<ArchivedRow>> WaitForArchivedAsync(HostInfrastructure infrastructure, long groupId, int expectedRows)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(ArchiveTimeout);
        while (true)
        {
            List<ArchivedRow> rows = await ReadRowsAsync(infrastructure, groupId);
            if (rows.Count >= expectedRows && rows.All(row => !row.Flags.HasFlag(ChatArchiveFlags.MediaPending)))
            {
                Assert.Equal(expectedRows, rows.Count);
                return rows;
            }

            await Task.Delay(PollInterval, timeout.Token);
        }
    }

    private async Task<List<ArchivedRow>> ReadRowsAsync(HostInfrastructure infrastructure, long groupId)
    {
        await using NpgsqlConnection connection = await infrastructure.Database.OpenConnectionAsync(Token);
        await using NpgsqlCommand command = new(
            $"select message_id, sender_id, text, segments::text, archive_flags from {AccountSchema}.chat_message " +
            "where conversation_type = @type and conversation_id = @group",
            connection);
        command.Parameters.AddWithValue("type", (short)ChatConversationType.Group);
        command.Parameters.AddWithValue("group", groupId);
        List<ArchivedRow> rows = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
            rows.Add(
                new ArchivedRow
                {
                    MessageId    = reader.GetInt64(0),
                    SenderId     = reader.GetInt64(1),
                    Text         = reader.IsDBNull(2) ? null : reader.GetString(2),
                    SegmentsJson = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Flags        = (ChatArchiveFlags)reader.GetInt16(4)
                });
        return rows;
    }

    private async Task<Dictionary<long, StatRow>> ReadStatsAsync(HostInfrastructure infrastructure, long groupId)
    {
        await using NpgsqlConnection connection = await infrastructure.Database.OpenConnectionAsync(Token);
        await using NpgsqlCommand command = new(
            $"select sender_id, sum(message_count), sum(char_count), sum(image_count) from {AccountSchema}.chat_stat_daily " +
            "where conversation_type = @type and conversation_id = @group group by sender_id",
            connection);
        command.Parameters.AddWithValue("type", (short)ChatConversationType.Group);
        command.Parameters.AddWithValue("group", groupId);
        Dictionary<long, StatRow> stats = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
            stats[reader.GetInt64(0)] = new StatRow
            {
                MessageCount = reader.GetInt64(1),
                CharCount    = reader.GetInt64(2),
                ImageCount   = reader.GetInt64(3)
            };
        return stats;
    }

    private string AccountSchema => _fixture.Schemas.Account(InfrastructureFixture.SELF_ID);

    private static long MediaId(JsonNode? segment)
    {
        JsonNode? id = segment?["m"];
        Assert.NotNull(id);
        return id.GetValue<long>();
    }

    // PNG 文件头加随机内容，保证每张图片内容不同。
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Guid.NewGuid().ToByteArray()];

    private static JsonObject Text(string text) => Segment("text", new JsonObject { ["text"] = text });

    private static JsonObject Mention(long userId) =>
        Segment(
            "mention",
            new JsonObject
            {
                ["user_id"] = userId,
                ["name"]    = "someone"
            });

    private static JsonObject Image(string url) =>
        Segment(
            "image",
            new JsonObject
            {
                ["resource_id"] = "",
                ["temp_url"]    = url,
                ["width"]       = 1,
                ["height"]      = 1,
                ["summary"]     = "",
                ["sub_type"]    = "normal"
            });

    private static JsonObject MarketFace(string url) =>
        Segment(
            "market_face",
            new JsonObject
            {
                ["emoji_package_id"] = 1,
                ["emoji_id"]         = "e1",
                ["key"]              = "k1",
                ["summary"]          = "[开心]",
                ["url"]              = url
            });

    private static JsonObject Forward(string forwardId) =>
        Segment(
            "forward",
            new JsonObject
            {
                ["forward_id"] = forwardId,
                ["title"]      = "群聊的聊天记录",
                ["preview"]    = new JsonArray(),
                ["summary"]    = ""
            });

    private static JsonObject Segment(string type, JsonObject data) =>
        new()
        {
            ["type"] = type,
            ["data"] = data
        };

    private sealed class ArchivedRow
    {
        public required long MessageId { get; init; }

        public required long SenderId { get; init; }

        public required string? Text { get; init; }

        public required string? SegmentsJson { get; init; }

        public required ChatArchiveFlags Flags { get; init; }

        public JsonArray Segments()
        {
            Assert.NotNull(SegmentsJson);
            return Assert.IsType<JsonArray>(JsonNode.Parse(SegmentsJson));
        }
    }

    private sealed class StatRow
    {
        public required long MessageCount { get; init; }

        public required long CharCount { get; init; }

        public required long ImageCount { get; init; }
    }
}
