using Shinano.Plugin.Cheru;
using Shinano.Plugin.Entertainment;
using Sora.Core.Enums;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Tests.Plugins;

/// <summary>随机选择的消息段拆分、娱乐禁言的权限规则与切噜编解码。</summary>
public sealed class EntertainmentAndCheruTests
{
    private static readonly ImageSegment ImageA = SegmentBuilder.Image("https://example.invalid/a.png");
    private static readonly ImageSegment ImageB = SegmentBuilder.Image("https://example.invalid/b.png");

    [Fact]
    public void Choice_SplitsTextOptionsAndDropsEmptyOnes()
    {
        IReadOnlyList<MessageBody>? options = ChoiceParser.Parse(new MessageBody(" 选择 苹果 还是还是 香蕉 还是  "));

        Assert.NotNull(options);
        Assert.Equal(["苹果", "香蕉"], options.Select(option => option.GetText()));
    }

    [Fact]
    public void Choice_KeepsImagesAndMixedOptionsTogether()
    {
        IReadOnlyList<MessageBody>? images = ChoiceParser.Parse(new MessageBody([Text("选择"), ImageA, Text("还是"), ImageB]));
        IReadOnlyList<MessageBody>? mixed = ChoiceParser.Parse(new MessageBody([Text("选择苹果"), ImageA, Text(" 还是香蕉")]));

        Assert.NotNull(images);
        Assert.Equal([ImageA, ImageB], images.Select(option => Assert.Single(option)));
        Assert.NotNull(mixed);
        Assert.Equal(2, mixed[0].Count);
        Assert.Equal("苹果", mixed[0].GetText());
        Assert.Same(ImageA, mixed[0][1]);
        Assert.Equal("香蕉", Assert.Single(mixed[1]) is TextSegment text ? text.Text : null);
    }

    [Theory]
    [MemberData(nameof(InvalidChoices))]
    public void Choice_RejectsInvalidMessages(MessageBody body) => Assert.Null(ChoiceParser.Parse(body));

    public static TheoryData<MessageBody> InvalidChoices() =>
    [
        new MessageBody("选择苹果还是"),
        new MessageBody("我选择苹果还是香蕉"),
        new MessageBody([Text("选择苹果还是香蕉"), SegmentBuilder.Mention(new Sora.Core.Types.UserId(10001))]),
        new MessageBody([ImageA, Text("选择苹果还是香蕉")])
    ];

    [Theory]
    [InlineData(MemberRole.Owner, MemberRole.Admin, true)]
    [InlineData(MemberRole.Owner, MemberRole.Member, true)]
    [InlineData(MemberRole.Owner, MemberRole.Owner, false)]
    [InlineData(MemberRole.Admin, MemberRole.Member, true)]
    [InlineData(MemberRole.Admin, MemberRole.Admin, false)]
    [InlineData(MemberRole.Member, MemberRole.Member, false)]
    public void Mute_FollowsGroupRoleRules(MemberRole bot, MemberRole target, bool expected) =>
        Assert.Equal(expected, MutePermission.CanMute(bot, target));

    [Fact]
    public void Cheru_EncodesEveryNonWhitespaceRun()
    {
        // 切噜词格式与 HoshinoBot 相同；字母、数字与标点同样编码，空白原样保留。
        Assert.Equal("切哔巴咧蹦噜噼咧巴", CheruCodec.Encode("你好"));
        string[] words = CheruCodec.Encode("你好, world! 243141").Split(' ');
        Assert.Equal(3, words.Length);
        Assert.All(words, word => Assert.Matches("^切[切卟叮咧哔唎啪啰啵嘭噜噼巴拉蹦铃]+$", word));
    }

    [Theory]
    [InlineData("今天天气不错，去打球吗？OK")]
    [InlineData("你好abc 123")]
    [InlineData("https://www.bilibili.com/video/BV1qJx76CEip/?spm_id_from=333.1007")]
    public void Cheru_DecodeRestoresEncodedText(string text) => Assert.Equal(text, CheruCodec.Decode(CheruCodec.Encode(text)));

    [Fact]
    public void Cheru_DecodesOnlyWholeCheruWords()
    {
        // 与汉字相连的切噜字不是完整的切噜词，原样保留；长度为奇数时末尾落单的字原样保留。
        Assert.Equal("你好切卟叮", CheruCodec.Decode("你好切卟叮"));
        Assert.Equal("你好铃", CheruCodec.Decode("切哔巴咧蹦噜噼咧巴铃"));
    }

    private static TextSegment Text(string text) => SegmentBuilder.Text(text);
}
