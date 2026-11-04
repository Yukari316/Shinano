using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Shinano.Framework.Rendering.Models;
using Shinano.Plugin.Hso.Entities;
using Shinano.Plugin.Hso.Models;
using Sora.Command.Attributes;
using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Info;
using Sora.Entities.Message;
using MatchType = Sora.Core.Enums.MatchType;

namespace Shinano.Plugin.Hso;

/// <summary>
/// 发图与排行只在群聊，屏蔽的群中静默不响应（也不计冷却）。发图冷却按群与发送者 60 秒，从触发时开始计时；
/// 图片发出后才计入排行，图库为空、冷却中与发送失败都不计数。<c>AD</c> 导入仅 Super User，群聊与私聊可用。
/// </summary>
[CommandGroup(Name = "hso")]
public sealed partial class HsoCommands
{
    private const int    RANKING_SIZE  = 10;
    private const string BASE64_PREFIX = "base64://";

    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    private readonly CooldownTracker              _cooldown = new(TimeProvider.System);
    private readonly IPluginInitializationContext _context;
    private readonly HsoGallery                   _gallery;
    private readonly HsoGroups                    _groups;
    private readonly HsoInfoResolver              _info;
    private readonly HsoSender                    _sender;
    private readonly HsoPixivImporter             _importer;
    private readonly ILogger<HsoCommands>         _logger;

    /// <summary>创建指令组。</summary>
    public HsoCommands(
        IPluginInitializationContext context,
        HsoGallery                   gallery,
        HsoGroups                    groups,
        HsoInfoResolver              info,
        HsoSender                    sender,
        HsoPixivImporter             importer,
        ILogger<HsoCommands>         logger)
    {
        _context  = context;
        _gallery  = gallery;
        _groups   = groups;
        _info     = info;
        _sender   = sender;
        _importer = importer;
        _logger   = logger;
    }

    /// <summary>从图库随机发一张图，再发作品信息卡。</summary>
    [Command(
        Expressions = ["来点色图", "来点涩图", "我要看色图"],
        MatchType = MatchType.Full,
        SourceScope = MessageSourceScope.Group,
        Description = "随机发一张图库图片")]
    public async ValueTask DrawAsync(MessageReceivedEvent e)
    {
        GroupId groupId = e.Message.GroupId;
        HsoGroupSettingRecord setting = await _groups.SettingAsync(groupId);
        if (setting.Blocked) return;
        if (_cooldown.IsCooling(groupId, e.Message.SenderId, Cooldown))
        {
            await e.ReplyAsync(new MessageBody(ReplyTexts.COOLDOWN));
            return;
        }

        _cooldown.Record(groupId, e.Message.SenderId);
        if (await _gallery.PickAsync() is not { } record)
        {
            await e.ReplyAsync(new MessageBody(ReplyTexts.EMPTY));
            return;
        }

        if (record.InfoState == (int)HsoInfoState.Pending)
        {
            byte[]? content = record.Pid is null ? await _gallery.ReadAsync(record, CancellationToken.None) : null;
            await _gallery.ApplyLookupAsync(record, await _info.LookupAsync(record, content, CancellationToken.None));
        }

        if (await _sender.SendAsync(e, record, setting.AllowNsfw, CancellationToken.None))
        {
            await _groups.IncrementAsync(groupId, e.Message.SenderId);
            return;
        }

        await e.ReplyAsync(new MessageBody(ReplyTexts.SEND_FAILED));
    }

    /// <summary>本群发图次数排行（前 10 名），以排行卡片发送，不 @ 任何人。</summary>
    [Command(
        Expressions = ["来点色批"],
        MatchType = MatchType.Full,
        SourceScope = MessageSourceScope.Group,
        Description = "本群发图次数排行")]
    public async ValueTask RankingAsync(MessageReceivedEvent e)
    {
        GroupId groupId = e.Message.GroupId;
        if ((await _groups.SettingAsync(groupId)).Blocked) return;

        List<HsoUsageRecord> top = await _groups.TopAsync(groupId, RANKING_SIZE);
        if (top.Count == 0)
        {
            await e.ReplyAsync(new MessageBody(ReplyTexts.RANKING_EMPTY));
            return;
        }

        Dictionary<long, string> names = await MemberNamesAsync(e, groupId);
        RankingCard card = new()
        {
            Title    = ReplyTexts.RANKING_TITLE,
            Subtitle = $"本群 {await _groups.RankedCountAsync(groupId)} 人上榜",
            Entries =
            [
                .. top.Select(usage => new RankingEntry
                {
                    Name  = names.GetValueOrDefault(usage.UserId) ?? usage.UserId.ToString(CultureInfo.InvariantCulture),
                    Count = usage.Count
                })
            ]
        };

        MessageBody body;
        try
        {
            body = new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(_context.Images.RenderRanking(card)));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "绘制 Hso 排行失败，改发文字");
            body = new MessageBody(string.Join('\n', [card.Title, .. card.Entries.Select((entry, index) => $"{index + 1}. {entry.Name} {entry.Count}")]));
        }

        await e.ReplyAsync(body);
    }

    /// <summary><c>AD&lt;pid&gt; &lt;页号&gt;</c>：从 Pixiv 导入作品的指定页，页号从 0 开始。</summary>
    [Command(
        Expressions = [@"^AD\d+\s+\d+$"],
        MatchType = MatchType.Regex,
        SourceScope = MessageSourceScope.Group | MessageSourceScope.Friend,
        SuperUserOnly = true,
        Description = "从 Pixiv 导入图片到图库")]
    public async ValueTask ImportAsync(MessageReceivedEvent e)
    {
        Match match = AdCommand().Match(e.Message.Body.GetText().Trim());
        if (!match.Success
            || !long.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long pid)
            || !int.TryParse(match.Groups["page"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int page))
        {
            await e.ReplyAsync(new MessageBody(ReplyTexts.AD_NOT_FOUND));
            return;
        }

        HsoPixivImportOutcome outcome = await _importer.ImportAsync(pid, page, CancellationToken.None);
        _logger.LogInformation("Super User {UserId} 导入 Pixiv 作品 {Pid} 第 {Page} 页：{Outcome}", e.Message.SenderId, pid, page, outcome);
        await e.ReplyAsync(new MessageBody(outcome switch
        {
            HsoPixivImportOutcome.Imported => ReplyTexts.AD_IMPORTED,
            HsoPixivImportOutcome.Exists   => ReplyTexts.AD_EXISTS,
            HsoPixivImportOutcome.NotFound => ReplyTexts.AD_NOT_FOUND,
            _                              => ReplyTexts.AD_FAILED
        }));
    }

    // 群名片优先，没有时用昵称；取不到时显示 QQ 号。
    private async Task<Dictionary<long, string>> MemberNamesAsync(MessageReceivedEvent e, GroupId groupId)
    {
        ApiResult<IReadOnlyList<GroupMemberInfo>> members = await e.Api.GetGroupMemberListAsync(groupId, false);
        if (members is { IsSuccess: true, Data: { } list })
            return list.ToDictionary(member => (long)member.UserId, member => string.IsNullOrEmpty(member.Card) ? member.Nickname : member.Card);

        _logger.LogWarning("获取群 {GroupId} 成员列表失败：{Code} {Message}", groupId, members.Code, members.Message);
        return [];
    }

    [GeneratedRegex(@"^AD(?<pid>\d{1,12})\s+(?<page>\d{1,4})$")]
    private static partial Regex AdCommand();
}
