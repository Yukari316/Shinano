using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Shinano.Plugin.ImageSources;
using Shinano.Plugin.ImageSources.Gateway;
using Shinano.Plugin.ImageSources.Parsing;
using Sora.Command.Attributes;
using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Message;
using Sora.Entities.MessageWaiting;
using Sora.Entities.Segments;
using MatchType = Sora.Core.Enums.MatchType;

namespace Shinano.Plugin.ImageSearch;

/// <summary>
/// 「搜图」与「搜番」，群聊与私聊可用。图片取自指令消息本身、引用的消息，或在配置的时间内等待发起人发图
/// （其他消息不被消耗；等待期间再次发送指令由 Sora 防重入占用，不另行提示）。
/// 冷却按会话与发送者、两条指令各自 20 秒，从回复结果后开始计时：搜索本身可能耗时较长，从发指令开始计时则搜完时冷却已过。
/// </summary>
[CommandGroup(Name = "image-search")]
public sealed class ImageSearchCommands
{
    private const int SECONDS_PER_MINUTE = 60;
    private const int IQDB_ATTEMPTS      = 2;

    private static readonly TimeSpan _cooldown = TimeSpan.FromSeconds(20);

    private readonly CooldownTracker              _imageCooldown = new(TimeProvider.System);
    private readonly CooldownTracker              _animeCooldown = new(TimeProvider.System);
    private readonly IPluginInitializationContext _context;
    private readonly YukariGateway                _gateway;
    private readonly QueryImageReader             _reader;
    private readonly ResultSender                 _sender;
    private readonly ILogger<ImageSearchCommands> _logger;

    /// <summary>创建指令组。</summary>
    public ImageSearchCommands(
        IPluginInitializationContext context,
        YukariGateway                gateway,
        QueryImageReader             reader,
        ResultSender                 sender,
        ILogger<ImageSearchCommands> logger)
    {
        _context = context;
        _gateway = gateway;
        _reader  = reader;
        _sender  = sender;
        _logger  = logger;
    }

    private ImageSearchConfig Config => _context.Config<ImageSearchConfig>().Value;

    /// <summary>
    /// 以图搜图：SauceNAO 的插画图库为主，最高结果低于阈值时再检索全部库并合并结果（可在插件配置关闭）；SauceNAO 不可用时先提示，再以 IQDB 兜底。
    /// </summary>
    [Command(
        Expressions = ["搜图"],
        MatchType = MatchType.Full,
        SourceScope = MessageSourceScope.Group | MessageSourceScope.Friend,
        Description = "以图搜图")]
    public ValueTask SearchImageAsync(MessageReceivedEvent e) =>
        RunAsync(e, _imageCooldown, ReplyTexts.RECEIVED_IMAGE, async image =>
        {
            ImageSearchConfig config = Config;
            SearchResult sauceNao = await SearchSauceNaoAsync(SearchDatabases.SauceNaoIllustrations, image);
            SearchResult? iqdb = null;
            if (SearchDecisions.NeedsFallback(sauceNao))
            {
                await e.ReplyAsync(new MessageBody(ReplyTexts.TRYING_FALLBACK));
                iqdb = await SearchIqdbAsync(image);
            }
            else if (config.FullSearchBelowThreshold && SearchDecisions.NeedsFullSearch(sauceNao, config.SauceNaoThreshold))
            {
                _logger.LogInformation(
                    "SauceNAO 插画图库的最高相似度 {Similarity} 低于阈值 {Threshold}%，再检索全部库并合并结果",
                    sauceNao.Hits.FirstOrDefault() is { } best ? string.Create(CultureInfo.InvariantCulture, $"{best.Similarity:0.##}%") : "无",
                    config.SauceNaoThreshold);
                sauceNao = SearchDecisions.Merge(sauceNao, await SearchSauceNaoAsync(null, image));
            }
            SearchDecision decision = SearchDecisions.ForImage(sauceNao, iqdb, config.SauceNaoThreshold, config.IqdbThreshold);
            Log("搜图", decision, sauceNao, iqdb);
            return decision;
        });

    /// <summary>以图搜番：trace.moe 为主，trace.moe 不可用时先提示，再以 SauceNAO 的番剧库兜底。</summary>
    [Command(
        Expressions = ["搜番"],
        MatchType = MatchType.Full,
        SourceScope = MessageSourceScope.Group | MessageSourceScope.Friend,
        Description = "以图搜番")]
    public ValueTask SearchAnimeAsync(MessageReceivedEvent e) =>
        RunAsync(e, _animeCooldown, ReplyTexts.RECEIVED_ANIME, async image =>
        {
            ImageSearchConfig config = Config;
            SearchResult traceMoe = await SearchAsync(
                SearchEngine.TraceMoe,
                () => _gateway.SearchTraceMoeAsync(image.Content, image.MediaType, CancellationToken.None),
                TraceMoeParser.Parse);
            SearchResult? sauceNao = null;
            if (SearchDecisions.NeedsFallback(traceMoe))
            {
                await e.ReplyAsync(new MessageBody(ReplyTexts.TRYING_FALLBACK));
                sauceNao = await SearchSauceNaoAsync(SearchDatabases.SauceNaoAnime, image);
            }
            SearchDecision decision = SearchDecisions.ForAnime(traceMoe, sauceNao, config.AnimeThreshold, config.SauceNaoThreshold);
            Log("搜番", decision, traceMoe, sauceNao);
            return decision;
        });

    // 冷却中只回复提示；取得图片后先回复确认收到（用户的图片可能被腾讯拦截而 Bot 收不到），
    // 搜索并回复完结果才开始计时（没有搜索就结束的不计时）。
    private async ValueTask RunAsync(
        MessageReceivedEvent                      e,
        CooldownTracker                           cooldown,
        string                                    received,
        Func<QueryImage, Task<SearchDecision>>    search)
    {
        GroupId groupId = e.Message.SourceType == MessageSourceType.Group ? e.Message.GroupId : default;
        if (cooldown.IsCooling(groupId, e.Message.SenderId, _cooldown))
        {
            await e.ReplyAsync(new MessageBody(ReplyTexts.COOLDOWN));
            return;
        }

        if (await ReadQueryAsync(e) is not { } image) return;
        await e.ReplyAsync(new MessageBody(received));
        try
        {
            await ReplyAsync(e, await search(image));
        }
        finally
        {
            cooldown.Record(groupId, e.Message.SenderId);
        }
    }

    // 未配置令牌时直接回复；没有附图时提示并等待发图，超时回复后返回 null；图片下载失败按服务出错回复。
    private async Task<QueryImage?> ReadQueryAsync(MessageReceivedEvent e)
    {
        if (!_gateway.IsConfigured)
        {
            await e.ReplyAsync(new MessageBody(ReplyTexts.UNCONFIGURED));
            return null;
        }

        ImageSegment? segment = await _reader.FindAsync(e);
        if (segment is null)
        {
            int waitSeconds = Config.WaitSeconds;
            await e.ReplyAsync(new MessageBody(string.Format(ReplyTexts.WAITING, Duration(waitSeconds))));
            MessageReceivedEvent? next = await e.WaitForNextMessageAsync(QueryImageReader.HasImage, TimeSpan.FromSeconds(waitSeconds));
            segment = next?.Message.Body.OfType<ImageSegment>().FirstOrDefault();
            if (segment is null)
            {
                await e.ReplyAsync(new MessageBody(ReplyTexts.NO_IMAGE));
                return null;
            }
        }

        QueryImage? image = await _reader.DownloadAsync(segment, CancellationToken.None);
        if (image is null) await e.ReplyAsync(new MessageBody(ReplyTexts.SERVER_ERROR));
        return image;
    }

    private async Task ReplyAsync(MessageReceivedEvent e, SearchDecision decision)
    {
        switch (decision.Hit)
        {
            case null:
                await e.ReplyAsync(new MessageBody(decision.Reply));
                break;
            case { Kind: HitKind.Anime } anime:
                await _sender.SendAnimeAsync(e, anime, decision.Threshold, decision.LowSimilarity, CancellationToken.None);
                break;
            case { } illustration:
                await _sender.SendIllustrationAsync(e, illustration, decision.Threshold, decision.LowSimilarity, CancellationToken.None);
                break;
        }
    }

    // databases 为 null 时检索全部库。
    private Task<SearchResult> SearchSauceNaoAsync(IReadOnlySet<SauceNaoIndex>? databases, QueryImage image) =>
        SearchAsync(
            SearchEngine.SauceNao,
            () => _gateway.SearchSauceNaoAsync(databases, image.Content, image.MediaType, CancellationToken.None),
            response => SauceNaoParser.Parse(response, databases));

    // IQDB 排队后间歇性返回读取失败页，不计额度，重试一次；网关出错或超时不重试，避免等待时间翻倍。
    private async Task<SearchResult> SearchIqdbAsync(QueryImage image)
    {
        for (int attempt = 1; ; attempt++)
        {
            bool readFailure = false;
            SearchResult result = await SearchAsync(
                SearchEngine.Iqdb,
                () => _gateway.SearchIqdbAsync(SearchDatabases.IqdbIllustrations, image.Content, image.MediaType, CancellationToken.None),
                response =>
                {
                    readFailure = IqdbParser.IsReadFailure(response);
                    return IqdbParser.Parse(response, SearchDatabases.IqdbIllustrations);
                });
            if (!readFailure || attempt >= IQDB_ATTEMPTS) return result;
            _logger.LogInformation("IQDB 返回读取失败页，第 {Attempt} 次重试", attempt);
        }
    }

    // 调用一个搜索服务并记录其结果：HTTP 状态码、耗时、最高相似度与其标题。
    private async Task<SearchResult> SearchAsync(SearchEngine engine, Func<Task<GatewayResponse>> send, Func<GatewayResponse, SearchResult> parse)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        GatewayResponse response = await send();
        elapsed.Stop();
        SearchResult result = parse(response);
        SearchHit? best = result.Hits.FirstOrDefault();
        _logger.LogInformation(
            "{Engine} 搜索：HTTP {StatusCode}，{Seconds:0.##} 秒，最高相似度 {Similarity}，标题 {Title}",
            engine,
            response.StatusCode,
            elapsed.Elapsed.TotalSeconds,
            best is null ? "无" : string.Create(CultureInfo.InvariantCulture, $"{best.Similarity:0.##}%"),
            best?.Title ?? "无");
        return result;
    }

    private void Log(string command, SearchDecision decision, SearchResult primary, SearchResult? fallback) =>
        _logger.LogInformation(
            "{Command}：主服务 {Primary}（{PrimaryDetail}），兜底 {Fallback}（{FallbackDetail}），结果 {Source} {Similarity:0.##}%{Low}",
            command,
            primary.Status,
            primary.Detail,
            fallback?.Status.ToString() ?? "未查询",
            fallback?.Detail ?? string.Empty,
            decision.Hit?.SourceName ?? decision.Reply,
            decision.Hit?.Similarity ?? 0,
            decision.LowSimilarity ? "（低于阈值）" : string.Empty);

    // 等待时长写成「1分钟」或「90秒」。
    private static string Duration(int seconds) =>
        seconds % SECONDS_PER_MINUTE == 0 ? $"{seconds / SECONDS_PER_MINUTE}分钟" : $"{seconds}秒";
}
