using Microsoft.Extensions.Logging.Abstractions;
using Shinano.Plugin.Qa;
using Shinano.Plugin.Qa.Models;
using Sora.Core.Types;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Tests.Plugins;

/// <summary>有人问的录入解析、匹配优先级与正则超时、回答拆分与列表分页。</summary>
public sealed class QaTests
{
    private static readonly ImageSegment Image = SegmentBuilder.Image("https://example.invalid/a.png");

    [Fact]
    public void ParseInline_SplitsAtFirstMarkAndTrims()
    {
        QaParseResult result = QaRecordingParser.ParseInline(
            new MessageBody([Text(" 有人问 今天吃什么 你答 吃"), Image, Text("你答 "), SegmentBuilder.Mention(new UserId(10001))]),
            QaRecordingParser.INLINE_PREFIX);

        Assert.Equal(QaParseError.None, result.Error);
        Assert.Equal("今天吃什么", result.Question);
        Assert.Equal([QaPartKind.Text, QaPartKind.Image, QaPartKind.Text, QaPartKind.Mention], result.Answer.Select(part => part.Kind));
        Assert.Equal("吃", result.Answer[0].Text);
        Assert.Equal(10001, result.Answer[3].UserId);
    }

    [Theory]
    [MemberData(nameof(InvalidInline))]
    public void ParseInline_RejectsInvalidMessages(MessageBody body, QaParseError expected) =>
        Assert.Equal(expected, QaRecordingParser.ParseInline(body, QaRecordingParser.INLINE_PREFIX).Error);

    public static TheoryData<MessageBody, QaParseError> InvalidInline() =>
        new()
        {
            { new MessageBody("有人问你答回答"), QaParseError.Malformed },
            { new MessageBody("有人问问题你答  "), QaParseError.Malformed },
            { new MessageBody("有人问问题"), QaParseError.Malformed },
            { new MessageBody([Text("有人问"), Image, Text("你答回答")]), QaParseError.QuestionNotText },
            { new MessageBody([Text("有人问问题你答"), SegmentBuilder.MentionAll()]), QaParseError.Unsupported },
            { new MessageBody([Text("有人问问题你答"), SegmentBuilder.Audio("https://example.invalid/a.amr")]), QaParseError.Unsupported }
        };

    [Fact]
    public void ParseQuote_AcceptsReplyWithAutoMentionAndKeepsWholeQuotedMessage()
    {
        ReplySegment reply = SegmentBuilder.Reply(new MessageId(42));
        QaParseResult command = QaRecordingParser.ParseQuoteCommand(
            new MessageBody([reply, SegmentBuilder.Mention(new UserId(10001)), Text(" 记录有人问 群规 ")]),
            QaRecordingParser.QUOTE_PREFIX);
        QaParseResult answer = QaRecordingParser.ParseQuotedAnswer(
            command.Question,
            new MessageBody([SegmentBuilder.Reply(new MessageId(7)), Text("看这个"), SegmentBuilder.Audio("https://example.invalid/a.amr")]));

        Assert.Equal(QaParseError.None, command.Error);
        Assert.Equal("群规", command.Question);
        Assert.Same(reply, command.Reply);
        Assert.Equal(QaParseError.None, answer.Error);
        Assert.Equal([QaPartKind.Text, QaPartKind.Audio], answer.Answer.Select(part => part.Kind));
        Assert.Equal(
            QaParseError.Unsupported,
            QaRecordingParser.ParseQuotedAnswer("群规", new MessageBody([Text("看"), SegmentBuilder.MentionAll()])).Error);
    }

    [Fact]
    public void Match_PrefersNewestAcrossMatchTypes()
    {
        QaMatcher matcher = new(NullLogger<QaMatcher>.Instance);
        QaEntry newerRegex = Entry(2, QaMatchType.Regex, "^早");
        QaEntry olderFull = Entry(1, QaMatchType.Full, "早上好");

        Assert.Same(newerRegex, matcher.Match([newerRegex, olderFull], "早上好", TimeSpan.FromMilliseconds(100)));
        Assert.Same(olderFull, matcher.Match([Entry(3, QaMatchType.Regex, "^晚"), olderFull], "早上好", TimeSpan.FromMilliseconds(100)));
        Assert.Null(matcher.Match([olderFull], "早上好啊", TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void Match_TreatsRegexTimeoutAsMiss()
    {
        QaMatcher matcher = new(NullLogger<QaMatcher>.Instance);
        QaEntry catastrophic = Entry(2, QaMatchType.Regex, "^(a+)+$");
        QaEntry fallback = Entry(1, QaMatchType.Regex, "a!$");

        Assert.Same(fallback, matcher.Match([catastrophic, fallback], new string('a', 40) + "!", TimeSpan.FromMilliseconds(10)));
        Assert.NotNull(QaMatcher.Validate("(未闭合"));
    }

    [Fact]
    public void Split_KeepsInlinePartsTogetherAndMediaStandalone()
    {
        QaAnswerPart[] answer =
        [
            Part(QaPartKind.Text), Part(QaPartKind.Image), Part(QaPartKind.Audio), Part(QaPartKind.Mention), Part(QaPartKind.Text),
            Part(QaPartKind.File), Part(QaPartKind.Video)
        ];

        IReadOnlyList<IReadOnlyList<QaAnswerPart>> units = QaAnswerSender.Split(answer);

        Assert.Equal([2, 1, 2, 1, 1], units.Select(unit => unit.Count));
        Assert.Equal([QaPartKind.Audio, QaPartKind.File, QaPartKind.Video], units.Where(unit => unit.Count == 1).Select(unit => unit[0].Kind));
    }

    [Fact]
    public void ListPage_NumbersAcrossPagesAndTagsRegex()
    {
        QaEntry[] entries = [.. Enumerable.Range(1, 7).Select(index => Entry(index, index % 2 == 0 ? QaMatchType.Regex : QaMatchType.Full, $"q{index}"))];

        QaListPage? second = QaListPage.Take(entries, 2, 3);

        Assert.NotNull(second);
        Assert.Equal((2, 3, 7), (second.Number, second.Count, second.Total));
        Assert.Equal([4, 5, 6], second.ToCard().Items.Select(item => item.Index));
        Assert.Equal(["RG", null, "RG"], second.ToCard().Items.Select(item => item.Tag));
        Assert.Null(QaListPage.Take(entries, 4, 3));
        Assert.Null(QaListPage.Take(entries, 0, 3));
    }

    private static TextSegment Text(string text) => SegmentBuilder.Text(text);

    private static QaAnswerPart Part(QaPartKind kind) =>
        new()
        {
            Kind    = kind,
            MediaId = 1
        };

    private static QaEntry Entry(long id, QaMatchType matchType, string question) =>
        new()
        {
            Id        = id,
            GroupId   = 100,
            MatchType = matchType,
            Question  = question,
            Answer    = [new QaAnswerPart { Kind = QaPartKind.Text, Text = "a" }],
            MediaKey  = "k",
            CreatedBy = 0,
            CreatedAt = DateTimeOffset.UnixEpoch.AddMinutes(id),
            Version   = 1
        };
}
