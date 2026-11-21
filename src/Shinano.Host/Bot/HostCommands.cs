using Microsoft.Extensions.Logging;
using Shinano.Framework.Commands;
using Shinano.Framework.Rendering;
using Shinano.Host.Plugins;
using Shinano.Host.Rendering;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Logging;
using Sora.Command.Attributes;
using Sora.Entities.Events;
using Sora.Entities.Message;

namespace Shinano.Host.Bot;

/// <summary>宿主内置的运维指令，不属于任何插件，数据库离线时仍可用。</summary>
[CommandGroup(Name = "host")]
public sealed class HostCommands
{
    private const long   BYTES_PER_MIB          = 1024L * 1024;
    private const string BASE64_PREFIX          = "base64://";
    private const string TITLE                  = "健康检查";
    private const string RENDER_TEST_NOTE       = "Markdown 渲染测试";
    private const string RENDER_TEST_FOOTER     = "HOST DEBUG";
    private const string RENDER_TEST_TRIGGER    = "渲染测试";
    private const string RENDER_TEST_ALIAS      = "md测试";
    private const string DEFAULT_MARKDOWN_SAMPLE = """
        # Markdown 渲染测试

        本卡片用于验证宿主的 **粗体**、*高亮强调*、~~删除线~~、`行内代码` 与 [文档链接](https://example.com) 排版效果。

        ## 行内与块级 LaTeX 公式

        质能方程 $E = mc^2$ 与欧拉恒等式 \(e^{i\pi} + 1 = 0\) 应与正文同基线排布，复杂分数如 $\frac{-b \pm \sqrt{b^2 - 4ac}}{2a}$ 会自动扩展行高。

        含中文的公式 $\text{速度} = \frac{\text{路程}}{\text{时间}}$ 使用中文后备字体排版。

        $$
        \int_{-\infty}^{+\infty} e^{-x^2}\,dx = \sqrt{\pi}, \qquad \sum_{n=1}^{\infty} \frac{1}{n^2} = \frac{\pi^2}{6}
        $$

        ## 终末地暗色代码高亮

        ```csharp
        public sealed class GreetingService
        {
            private const int MAX_RETRY = 3;

            // 返回格式化问候语
            public static string Build(string name, int round) =>
                $"Hello, {name}! Round [{round}/{MAX_RETRY}]";
        }
        ```

        ```python
        def fibonacci(limit: int) -> list[int]:
            # 生成不超过上限的斐波那契数列
            seq = [0, 1]
            while seq[-1] + seq[-2] <= limit:
                seq.append(seq[-1] + seq[-2])
            return seq
        ```

        ## 列表、引用与表格

        > 终末地风格引用块：支持嵌套段落与行内公式 $\alpha + \beta = \gamma$。

        - [x] 语法高亮（TextMateSharp）
        - [x] LaTeX 数学公式（CSharpMath）
        - [ ] 持续迭代更多样例

        | 项目 | 状态 | 数值 |
        | --- | :---: | ---: |
        | 宿主文档卡片 | 正常 | 1200 |
        | 公式容错回退 | 正常 | 100% |
        | 单元格公式 | $$a^2 + b^2 = c^2$$ | 3 |

        ---

        非法公式容错测试：$\invalidmacro{\frac{1}$ 应回退为纯文本而不影响整卡渲染。
        """;

    private static readonly HealthComponent[] ReportedComponents =
        [HealthComponent.Database, HealthComponent.ObjectStorage, HealthComponent.Bot];

    private readonly HostInfrastructure   _infrastructure;
    private readonly RuntimeHealth        _health;
    private readonly PluginRuntime        _plugins;
    private readonly LogFileLayout        _logLayout;
    private readonly int                  _maxLogSizeMiB;
    private readonly CardRenderer         _images;
    private readonly TimeProvider         _time;
    private readonly TimeZoneInfo         _timeZone;
    private readonly ILogger<HostCommands> _logger;

    /// <summary>创建指令组。</summary>
    public HostCommands(
        HostInfrastructure    infrastructure,
        RuntimeHealth         health,
        PluginRuntime         plugins,
        LogFileLayout         logLayout,
        int                   maxLogSizeMiB,
        CardRenderer          images,
        TimeProvider          time,
        TimeZoneInfo          timeZone,
        ILogger<HostCommands> logger)
    {
        _infrastructure = infrastructure;
        _health         = health;
        _plugins        = plugins;
        _logLayout      = logLayout;
        _maxLogSizeMiB  = maxLogSizeMiB;
        _images         = images;
        _time           = time;
        _timeZone       = timeZone;
        _logger         = logger;
    }

    /// <summary>
    /// 健康检查：强制探测数据库与 RustFS，以状态报告卡片回复数据库、RustFS、LLBot 连接、日志目录占用与已启用的插件
    /// （含初始化失败的）。仅 Super User 可用，群聊与私聊均可；绘制失败时改发文字。
    /// </summary>
    [Command(Expressions = ["健康检查"], SuperUserOnly = true, Description = "强制检查宿主依赖并回复结果")]
    public async ValueTask HealthCheckAsync(MessageReceivedEvent e)
    {
        await _infrastructure.DatabaseAvailability.CheckAsync(force: true, CancellationToken.None);
        await _infrastructure.StorageAvailability.CheckAsync(force: true, CancellationToken.None);

        IReadOnlyList<StatusItem> items = await ItemsAsync();
        await e.ReplyAsync(Compose(items));
    }

    /// <summary>
    /// 渲染测试：渲染内置综合 Markdown 样例或紧随其后的自定义 Markdown，用于验证宿主文档卡片的代码高亮与 LaTeX 公式效果。
    /// 仅 Super User 可用，群聊与私聊均可。
    /// </summary>
    [Command(
        Expressions   = [@"^渲染测试(?:\s+[\s\S]+)?$", @"^md测试(?:\s+[\s\S]+)?$"],
        MatchType     = Sora.Core.Enums.MatchType.Regex,
        SuperUserOnly = true,
        Description   = "测试宿主 Markdown 文档卡片渲染")]
    public async ValueTask RenderMarkdownTestAsync(MessageReceivedEvent e)
    {
        string markdown = ExtractMarkdownSample(e.Message.Body.GetText());
        await e.ReplyAsync(ComposeDocument(markdown));
    }

    /// <summary>从指令文本中提取自定义 Markdown；为空时返回内置综合样例。</summary>
    internal static string ExtractMarkdownSample(string rawText)
    {
        string trimmed = rawText.Trim();
        string body = trimmed.StartsWith(RENDER_TEST_TRIGGER, StringComparison.OrdinalIgnoreCase)
            ? trimmed[RENDER_TEST_TRIGGER.Length..].Trim()
            : trimmed.StartsWith(RENDER_TEST_ALIAS, StringComparison.OrdinalIgnoreCase)
                ? trimmed[RENDER_TEST_ALIAS.Length..].Trim()
                : string.Empty;
        return body.Length > 0 ? body : DEFAULT_MARKDOWN_SAMPLE;
    }

    private MessageBody ComposeDocument(string markdown)
    {
        try
        {
            RenderedDocument rendered = _images.RenderDocument(
                new DocumentCard
                {
                    Markdown    = markdown,
                    Note        = RENDER_TEST_NOTE,
                    FooterRight = RENDER_TEST_FOOTER
                });
            return new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(rendered.Image));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "渲染 Markdown 测试卡片失败");
            return new MessageBody($"渲染失败：{exception.Message}");
        }
    }

    private static CardLevel ToCardLevel(HealthLevel level) =>
        level switch
        {
            HealthLevel.Stopped  => CardLevel.Error,
            HealthLevel.Degraded => CardLevel.Warning,
            _                    => CardLevel.Normal
        };

    private async Task<IReadOnlyList<StatusItem>> ItemsAsync()
    {
        List<StatusItem> items =
        [
            .. ReportedComponents.Select(_health.Get).Select(status => new StatusItem
            {
                Level  = ToCardLevel(status.Level),
                Name   = HealthText.ComponentName(status.Component),
                Value  = HealthText.Describe(status.Level),
                Detail = status.Detail.Length > 0 ? status.Detail : null
            })
        ];

        long logBytes = new DirectoryInfo(_logLayout.Directory).EnumerateFiles().Sum(file => file.Length);
        items.Add(
            new StatusItem
            {
                Level  = CardLevel.Normal,
                Name   = "日志目录",
                Value  = $"{logBytes / BYTES_PER_MIB} MiB",
                Detail = $"上限 {_maxLogSizeMiB} MiB"
            });

        IReadOnlyList<PluginSnapshot> plugins = await _plugins.SnapshotAsync(CancellationToken.None);
        items.AddRange(
            plugins.Where(plugin => plugin.Status != PluginStatus.Disabled)
                   .Select(plugin => new StatusItem
                   {
                       Level  = plugin.Status == PluginStatus.Active ? CardLevel.Normal : CardLevel.Warning,
                       Name   = plugin.Name,
                       Value = plugin.Status switch
                       {
                           PluginStatus.Active  => "已启用",
                           PluginStatus.Pending => "等待激活",
                           _                    => "失败"
                       },
                       Detail = plugin.Error is null ? $"v{plugin.Version}" : $"v{plugin.Version}，{plugin.Error}"
                   }));
        return items;
    }

    private MessageBody Compose(IReadOnlyList<StatusItem> items)
    {
        try
        {
            byte[] png = _images.RenderStatus(
                new StatusCard
                {
                    Title = TITLE,
                    Time  = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _timeZone),
                    Items = items
                });
            return new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(png));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "绘制健康检查卡片失败，改发文字");
            return new MessageBody(
                string.Join(
                    '\n',
                    [TITLE, .. items.Select(item => item.Detail is null ? $"{item.Name}：{item.Value}" : $"{item.Name}：{item.Value}（{item.Detail}）")]));
        }
    }
}
