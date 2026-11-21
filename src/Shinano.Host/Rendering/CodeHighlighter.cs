using System.Collections.Frozen;
using SkiaSharp;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Shinano.Host.Rendering;

/// <summary>
/// 基于 TextMateSharp 的代码语法高亮分词器，将 TextMate 作用域映射到参考 endfield-ui-skill 的终末地暗色工业风色板。
/// 线程安全；未知语言自动回退为统一的浅银文字。
/// </summary>
internal static class CodeHighlighter
{
    /// <summary>代码块冷工业暗灰底色（参考 endfield-ui-skill）。</summary>
    public static readonly SKColor Background = SKColor.Parse("#272C33");

    /// <summary>代码块顶栏底色。</summary>
    public static readonly SKColor HeaderBackground = SKColor.Parse("#1E2228");

    /// <summary>代码块边框色。</summary>
    public static readonly SKColor Border = SKColor.Parse("#3A414B");

    /// <summary>默认前景：高对比浅银文字。</summary>
    public static readonly SKColor DefaultForeground = SKColor.Parse("#E2E6EC");

    /// <summary>注释：低饱和冷灰。</summary>
    private static readonly SKColor CommentColor = SKColor.Parse("#7E8899");

    /// <summary>关键字与存储修饰：工业黄（endfield-ui-skill 主强调色）。</summary>
    private static readonly SKColor KeywordColor = SKColor.Parse("#FFD429");

    /// <summary>函数与方法名：克莱因蓝（endfield-ui-skill 次强调色）。</summary>
    private static readonly SKColor FunctionColor = SKColor.Parse("#6E8DEA");

    /// <summary>类型、类与接口：冷青色。</summary>
    private static readonly SKColor TypeColor = SKColor.Parse("#5CE1E6");

    /// <summary>字符串与正则：语义绿。</summary>
    private static readonly SKColor StringColor = SKColor.Parse("#7BD88F");

    /// <summary>数字、布尔与常量：琥珀橙。</summary>
    private static readonly SKColor ConstantColor = SKColor.Parse("#FF9E64");

    /// <summary>HTML/XML 标签与错误高亮：语义玫红。</summary>
    private static readonly SKColor TagOrErrorColor = SKColor.Parse("#F4707A");

    /// <summary>属性与参数名：冰蓝浅灰。</summary>
    private static readonly SKColor PropertyColor = SKColor.Parse("#B9C9F0");

    /// <summary>运算符与标点：中性银灰。</summary>
    private static readonly SKColor PunctuationColor = SKColor.Parse("#9AA5B5");

    private static readonly TimeSpan LineTimeout = TimeSpan.FromMilliseconds(200);

    // 常见 Markdown 围栏语言别名到 TextMate languageId 的映射。
    private static readonly FrozenDictionary<string, string> LanguageAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"]         = "csharp",
        ["c#"]         = "csharp",
        ["py"]         = "python",
        ["py3"]        = "python",
        ["js"]         = "javascript",
        ["mjs"]        = "javascript",
        ["cjs"]        = "javascript",
        ["jsx"]        = "javascriptreact",
        ["ts"]         = "typescript",
        ["tsx"]        = "typescriptreact",
        ["sh"]         = "shellscript",
        ["bash"]       = "shellscript",
        ["zsh"]        = "shellscript",
        ["shell"]      = "shellscript",
        ["ps1"]        = "powershell",
        ["pwsh"]       = "powershell",
        ["yml"]        = "yaml",
        ["md"]         = "markdown",
        ["c++"]        = "cpp",
        ["cc"]         = "cpp",
        ["cxx"]        = "cpp",
        ["h"]          = "c",
        ["hpp"]        = "cpp",
        ["rs"]         = "rust",
        ["rb"]         = "ruby",
        ["kt"]         = "kotlin",
        ["kts"]        = "kotlin",
        ["golang"]     = "go",
        ["dockerfile"] = "docker",
        ["json5"]      = "jsonc",
        ["vue"]        = "html",
        ["htm"]        = "html"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly Lock                                Sync     = new();
    private static readonly RegistryOptions                     Options  = new(ThemeName.DarkPlus);
    private static readonly Registry                            Registry = new(Options);
    private static readonly Dictionary<string, IGrammar?>       Grammars = new(StringComparer.OrdinalIgnoreCase);

    // 按作用域前缀长度降序预排，保证更具体的作用域（如 support.type.property-name、keyword.operator.new）优先匹配。
    private static readonly ScopeColorRule[] ScopeColorRules = BuildScopeColorRules(
    [
        (CommentColor, ["comment"]),
        (StringColor, ["string", "markup.inserted"]),
        (ConstantColor, ["constant.numeric", "constant.language", "constant.character", "constant.other.color", "constant.regexp"]),
        (KeywordColor, [
            "keyword.operator.new",
            "keyword.operator.expression",
            "keyword.operator.wordlike",
            "keyword.operator.logical.python",
            "keyword.operator.sizeof",
            "keyword.operator.typeof",
            "keyword",
            "storage",
            "meta.preprocessor"
        ]),
        (PunctuationColor, ["keyword.operator", "punctuation"]),
        (FunctionColor, ["entity.name.function", "support.function", "meta.function-call"]),
        (TypeColor, [
            "entity.name.type",
            "entity.name.class",
            "entity.name.struct",
            "entity.name.interface",
            "entity.name.enum",
            "entity.other.inherited-class",
            "support.type",
            "support.class"
        ]),
        (TagOrErrorColor, ["entity.name.tag", "invalid", "markup.deleted"]),
        (PropertyColor, [
            "entity.other.attribute-name",
            "variable.parameter",
            "variable.other.property",
            "variable.other.object.property",
            "support.type.property-name",
            "meta.object-literal.key"
        ])
    ]);

    /// <summary>
    /// 按指定语言对代码逐行分词着色。未知语言或空语言返回单色默认前景行。
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<CodeToken>> Highlight(string code, string language)
    {
        string[] rawLines = code.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        // 去掉 Markdig 代码块末尾自带的单个空行。
        int count = rawLines.Length > 1 && rawLines[^1].Length == 0 ? rawLines.Length - 1 : rawLines.Length;
        List<IReadOnlyList<CodeToken>> output = new(count);

        lock (Sync)
        {
            IGrammar? grammar = ResolveGrammar(language);
            if (grammar is null)
            {
                for (int index = 0; index < count; index++)
                    output.Add(PlainLine(rawLines[index]));
                return output;
            }

            IStateStack? state = null;
            for (int index = 0; index < count; index++)
            {
                string line = rawLines[index];
                if (line.Length == 0)
                {
                    output.Add([]);
                    continue;
                }

                try
                {
                    ITokenizeLineResult result = grammar.TokenizeLine(line, state, LineTimeout);
                    state = result.RuleStack;
                    output.Add(ConvertTokens(line, result.Tokens));
                }
                catch
                {
                    output.Add(PlainLine(line));
                }
            }
        }

        return output;
    }

    private static IReadOnlyList<CodeToken> PlainLine(string line) =>
        line.Length == 0
            ? []
            : [new CodeToken { Text = line, Color = DefaultForeground }];

    private static IGrammar? ResolveGrammar(string rawLanguage)
    {
        string key = rawLanguage.Trim();
        if (key.Length == 0) return null;

        // 围栏信息可能带额外参数（如 "csharp title=foo"），只取首段语言标识。
        int spaceIndex = key.IndexOfAny([' ', '\t', '{', ',']);
        if (spaceIndex > 0) key = key[..spaceIndex];

        if (Grammars.TryGetValue(key, out IGrammar? cached)) return cached;

        string normalized = LanguageAliases.TryGetValue(key, out string? mapped) ? mapped : key.ToLowerInvariant();
        string? scope = Options.GetScopeByLanguageId(normalized)
                        ?? Options.GetScopeByExtension("." + normalized);
        IGrammar? grammar = scope is not null ? Registry.LoadGrammar(scope) : null;
        Grammars[key] = grammar;
        return grammar;
    }

    private static List<CodeToken> ConvertTokens(string line, IToken[] tokens)
    {
        List<CodeToken> segments = [];
        int cursor = 0;
        foreach (IToken token in tokens)
        {
            int start = Math.Clamp(token.StartIndex, 0, line.Length);
            int end = Math.Clamp(token.EndIndex, start, line.Length);
            if (start > cursor)
                Append(segments, line[cursor..start], DefaultForeground);
            if (end > start)
                Append(segments, line[start..end], MapColor(token.Scopes));
            cursor = Math.Max(cursor, end);
        }

        if (cursor < line.Length)
            Append(segments, line[cursor..], DefaultForeground);
        return segments;
    }

    private static void Append(List<CodeToken> segments, string text, SKColor color)
    {
        if (text.Length == 0) return;
        if (segments.Count > 0 && segments[^1].Color == color)
        {
            segments[^1] = new CodeToken
            {
                Text  = segments[^1].Text + text,
                Color = color
            };
            return;
        }

        segments.Add(new CodeToken
        {
            Text  = text,
            Color = color
        });
    }

    // TextMate 作用域从外到内排列，优先从最具体（末尾）的作用域匹配颜色。
    internal static SKColor MapColor(List<string> scopes)
    {
        for (int index = scopes.Count - 1; index >= 0; index--)
        {
            string scope = scopes[index];
            foreach (ScopeColorRule rule in ScopeColorRules)
            {
                if (scope.StartsWith(rule.Prefix, StringComparison.OrdinalIgnoreCase))
                    return rule.Color;
            }
        }

        return DefaultForeground;
    }

    private static ScopeColorRule[] BuildScopeColorRules((SKColor Color, string[] Prefixes)[] groups) =>
    [
        .. groups
           .SelectMany(group => group.Prefixes.Select(prefix => new ScopeColorRule
           {
               Prefix = prefix,
               Color  = group.Color
           }))
           .OrderByDescending(rule => rule.Prefix.Length)
    ];

    private sealed class ScopeColorRule
    {
        public required string  Prefix { get; init; }
        public required SKColor Color  { get; init; }
    }
}

/// <summary>代码行中的一段同色文本。</summary>
internal sealed class CodeToken
{
    /// <summary>文本内容。</summary>
    public required string Text { get; init; }

    /// <summary>前景色。</summary>
    public required SKColor Color { get; init; }
}
