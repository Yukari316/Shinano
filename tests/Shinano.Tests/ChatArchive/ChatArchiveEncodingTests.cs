using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shinano.Infrastructure.ChatArchive;
using Sora.Core.Enums;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Tests.ChatArchive;

public sealed class ChatArchiveEncodingTests
{
    private static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [Theory]
    [InlineData("你好，world! 123", 12)]
    [InlineData("👨‍👩‍👧‍👦🇨🇳👍🏽", 3)]
    [InlineData("e\u0301", 1)]
    [InlineData(" \t\r\n\u3000", 0)]
    [InlineData("", 0)]
    public void CountVisibleCharacters_CountsGraphemesWithoutWhitespace(string text, int expected) =>
        Assert.Equal(expected, ChatTextMetrics.CountVisibleCharacters(text));

    [Fact]
    public void Counts_OnlyTopLevel_MediaPerOccurrence_NonTextSegmentsHaveNoChars()
    {
        ImageSegment image = SegmentBuilder.Image("base64://AA==", ImageSubType.Normal);
        Segment[] segments =
        [
            SegmentBuilder.Text("ab c"),
            image,
            image,
            SegmentBuilder.Mention(42),
            SegmentBuilder.Face("1", false),
            SegmentBuilder.Reply(7),
            SegmentBuilder.Audio("base64://AA=="),
            SegmentBuilder.Forward(
                [
                    new ForwardedMessageNode
                    {
                        UserId     = 1,
                        SenderName = "x",
                        Segments   = new MessageBody([SegmentBuilder.Text("不计入"), SegmentBuilder.Image("base64://AA==", ImageSubType.Normal)])
                    }
                ],
                "",
                [],
                "",
                "")
        ];

        ChatMessageCounts counts = ChatMessageCounts.FromSegments(segments);

        Assert.Equal(3, counts.CharCount);
        Assert.Equal(2, counts.ImageCount);
        Assert.Equal(1, counts.AudioCount);
        Assert.Equal(0, counts.VideoCount);
        Assert.Equal(0, counts.FileCount);
    }

    [Fact]
    public void Encode_WritesCompactNodes_AndCollectsMediaWithoutStoringSources()
    {
        ChatSegmentEncoder encoder = new();
        JsonArray encoded = encoder.Encode(
            [
                SegmentBuilder.Text("看"),
                SegmentBuilder.Mention(42),
                SegmentBuilder.Image("base64://AA==", ImageSubType.Sticker)
            ],
            depth: 0);

        Assert.Equal("""[{"t":"text","v":"看"},{"t":"at","u":42},{"t":"img","sub":"sticker"}]""", encoded.ToJsonString(Relaxed));
        PendingChatMedia media = Assert.Single(encoder.PendingMedia);
        Assert.Equal(ChatMediaKind.Image, media.Source.Kind);
        Assert.Equal("base64://AA==", media.Source.FileUri);
        Assert.Same(encoded[2], media.Node);
        Assert.False(encoder.HasMissingContent);
    }

    [Fact]
    public void Encode_ExpandsInlineForwardsUpToMaxDepth_ThenMarksMissing()
    {
        Segment innermost = SegmentBuilder.Text("最深");
        for (int level = 0; level <= ChatSegmentEncoder.MAX_FORWARD_DEPTH; level++)
            innermost = Forward(innermost);

        ChatSegmentEncoder encoder = new();
        JsonArray encoded = encoder.Encode([innermost], depth: 0);

        JsonNode? node = encoded[0];
        for (int level = 0; level < ChatSegmentEncoder.MAX_FORWARD_DEPTH; level++)
        {
            // SAFE: 前几层都已内联展开。
            node = node!["msgs"]![0]!["s"]![0];
        }

        Assert.Equal("fwd", node?["t"]?.GetValue<string>());
        Assert.Null(node?["msgs"]);
        Assert.True(encoder.HasMissingContent);
        Assert.Empty(encoder.PendingForwards);
    }

    [Fact]
    public void GapTracker_MergesRepeatedOpen_AndRequeuesUnwrittenGaps()
    {
        ArchiveGapTracker tracker = new();
        DateTimeOffset start = DateTimeOffset.UtcNow;

        Assert.True(tracker.Open(ArchiveGapCause.DatabaseOffline, start));
        Assert.False(tracker.Open(ArchiveGapCause.DatabaseOffline, start.AddMinutes(1)));
        Assert.True(tracker.Open(ArchiveGapCause.Backlog, start.AddMinutes(2)));
        Assert.True(tracker.Close(ArchiveGapCause.DatabaseOffline, start.AddMinutes(5)));
        Assert.False(tracker.Close(ArchiveGapCause.DatabaseOffline, start.AddMinutes(6)));

        ArchiveGap gap = Assert.Single(tracker.TakeClosed());
        Assert.Equal(start, gap.StartedAt);
        Assert.Equal(start.AddMinutes(5), gap.EndedAt);
        Assert.Empty(tracker.TakeClosed());

        tracker.Requeue([gap]);
        tracker.CloseAll(start.AddMinutes(10));
        Assert.Equal(
            [ArchiveGapCause.DatabaseOffline, ArchiveGapCause.Backlog],
            tracker.TakeClosed().Select(item => item.Cause));
    }

    private static ForwardSegment Forward(Segment content) =>
        SegmentBuilder.Forward(
            [
                new ForwardedMessageNode
                {
                    UserId     = 1,
                    SenderName = "x",
                    Segments   = new MessageBody([content])
                }
            ],
            "",
            [],
            "",
            "");
}
