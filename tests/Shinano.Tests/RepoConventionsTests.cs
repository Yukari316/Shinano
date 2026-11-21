using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shinano.Tests;

/// <summary>
/// 仓库级约定静态契约测试：守卫 AGENTS.md 单文件上下文体积上限、手写 SQL 的 SQL_NEED 注释与插件表声明一致性。
/// </summary>
public sealed partial class RepoConventionsTests
{
    // Antigravity 单个规则文件在 24,000 字节处硬截断，预留 2 KB 余量避免新增条文时静默丢失后半段规则。
    private const int MAX_AGENTS_FILE_BYTES  = 22_000;
    private const int SQL_NEED_LOOKBACK_LINES = 6;
    private const int MAX_TABLE_NAME_LENGTH  = 63;

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".idea",
        ".vs",
        "agent_tmp",
        "artifacts",
        "bin",
        "node_modules",
        "obj"
    };

    [Fact]
    public void AgentsMarkdown_StaysWithinSingleFileContextBudget()
    {
        string root = FindRepoRoot();
        List<string> files = [];
        CollectFiles(root, "AGENTS.md", files);
        Assert.NotEmpty(files);

        List<string> oversized = [];
        foreach (string file in files)
        {
            long size = new FileInfo(file).Length;
            if (size > MAX_AGENTS_FILE_BYTES)
                oversized.Add($"{Path.GetRelativePath(root, file)} ({size} bytes > {MAX_AGENTS_FILE_BYTES})");
        }

        Assert.True(
            oversized.Count == 0,
            "以下 AGENTS.md 超过单文件体积上限，请拆分子目录规则避免被 Agent 截断：\n" + string.Join('\n', oversized));
    }

    [Fact]
    public void RawSql_CarriesSqlNeedComment()
    {
        string root = FindRepoRoot();
        List<string> sourceFiles = [];
        CollectFiles(Path.Combine(root, "src"), "*.cs", sourceFiles);
        CollectFiles(Path.Combine(root, "plugins"), "*.cs", sourceFiles);
        Assert.NotEmpty(sourceFiles);

        List<string> violations = [];
        foreach (string file in sourceFiles)
        {
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                if (!RawSqlCallPattern().IsMatch(line)) continue;
                if (IsThinSqlParameterHelper(lines, index)) continue;
                if (HasSqlNeedAbove(lines, index)) continue;
                if (HasSqlNeedOnReferencedConstant(lines, index)) continue;

                violations.Add($"{Path.GetRelativePath(root, file)}:{index + 1}: {line.Trim()}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "以下原生 SQL 调用缺少 SQL_NEED: 注释说明原因：\n" + string.Join('\n', violations));
    }

    [Fact]
    public void PluginManifestTables_MatchSugarTableEntitiesAndPrefix()
    {
        string root = FindRepoRoot();
        string pluginsRoot = Path.Combine(root, "plugins");
        List<string> manifests = [];
        CollectFiles(pluginsRoot, "plugin.json", manifests);
        Assert.NotEmpty(manifests);

        List<string> violations = [];
        foreach (string manifestPath in manifests)
        {
            string pluginDirectory = Path.GetDirectoryName(manifestPath)!; // SAFE: CollectFiles 返回的是绝对文件路径。
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            JsonElement rootElement = document.RootElement;
            string pluginId = rootElement.GetProperty("id").GetString() ?? string.Empty;
            string expectedPrefix = pluginId.Replace('-', '_') + "_";

            HashSet<string> declared = new(StringComparer.Ordinal);
            if (rootElement.TryGetProperty("tables", out JsonElement tablesElement)
                && tablesElement.ValueKind == JsonValueKind.Object)
            {
                CollectDeclaredTables(tablesElement, "global", declared);
                CollectDeclaredTables(tablesElement, "account", declared);
            }

            HashSet<string> entities = new(StringComparer.Ordinal);
            List<string> csFiles = [];
            CollectFiles(pluginDirectory, "*.cs", csFiles);
            foreach (string csFile in csFiles)
            {
                string content = File.ReadAllText(csFile);
                foreach (Match match in SugarTablePattern().Matches(content))
                    entities.Add(match.Groups["name"].Value);
            }

            string relativeManifest = Path.GetRelativePath(root, manifestPath);
            foreach (string table in declared)
            {
                if (!table.StartsWith(expectedPrefix, StringComparison.Ordinal)
                    || table.Length > MAX_TABLE_NAME_LENGTH
                    || !TableNamePattern().IsMatch(table))
                    violations.Add($"{relativeManifest}: 表名 {table} 不符合前缀 {expectedPrefix} 或命名约束");

                if (!entities.Contains(table))
                    violations.Add($"{relativeManifest}: 声明了表 {table}，但插件源码中未找到对应 [SugarTable]");
            }

            foreach (string table in entities)
            {
                if (!declared.Contains(table))
                    violations.Add($"{relativeManifest}: 实体声明了 [SugarTable(\"{table}\")]，但未写入 plugin.json 的 tables");
            }
        }

        Assert.True(
            violations.Count == 0,
            "以下插件表声明与实体定义不一致：\n" + string.Join('\n', violations));
    }

    private static void CollectDeclaredTables(JsonElement tablesElement, string groupName, HashSet<string> target)
    {
        if (!tablesElement.TryGetProperty(groupName, out JsonElement group) || group.ValueKind != JsonValueKind.Array)
            return;
        foreach (JsonElement item in group.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } name)
                target.Add(name);
        }
    }

    private static bool HasSqlNeedAbove(string[] lines, int lineIndex)
    {
        int start = Math.Max(0, lineIndex - SQL_NEED_LOOKBACK_LINES);
        for (int cursor = lineIndex; cursor >= start; cursor--)
        {
            if (lines[cursor].Contains("SQL_NEED:", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool HasSqlNeedOnReferencedConstant(string[] lines, int lineIndex)
    {
        string window = lineIndex + 1 < lines.Length
            ? lines[lineIndex] + " " + lines[lineIndex + 1]
            : lines[lineIndex];
        Match match = FirstArgIdentifierPattern().Match(window);
        if (!match.Success) return false;

        string identifier = match.Groups["id"].Value;
        Regex declaration = new($@"\b(?:const|readonly)\s+string\s+{Regex.Escape(identifier)}\b");
        for (int cursor = 0; cursor < lines.Length; cursor++)
        {
            if (declaration.IsMatch(lines[cursor]) && HasSqlNeedAbove(lines, cursor))
                return true;
        }

        return false;
    }

    private static bool IsThinSqlParameterHelper(string[] lines, int lineIndex)
    {
        Match match = NpgsqlCommandParamPattern().Match(lines[lineIndex]);
        if (!match.Success) return false;

        string paramName = match.Groups["id"].Value;
        int start = Math.Max(0, lineIndex - SQL_NEED_LOOKBACK_LINES);
        Regex paramSignature = new($@"\bstring\s+{Regex.Escape(paramName)}\b");
        for (int cursor = lineIndex - 1; cursor >= start; cursor--)
        {
            if (paramSignature.IsMatch(lines[cursor]))
                return true;
        }

        return false;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Shinano.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("无法定位包含 Shinano.slnx 的仓库根目录");
    }

    private static void CollectFiles(string directory, string searchPattern, List<string> target)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string file in Directory.EnumerateFiles(directory, searchPattern))
            target.Add(file);
        foreach (string sub in Directory.EnumerateDirectories(directory))
        {
            if (SkippedDirectories.Contains(Path.GetFileName(sub))) continue;
            CollectFiles(sub, searchPattern, target);
        }
    }

    [GeneratedRegex(@"(?:\.Ado\.(?:ExecuteCommand|SqlQuery|GetScalar|GetDataTable|GetInt|GetLong|GetString)\w*(?:<[^>]+>)?\s*\(|\bnew\s+NpgsqlCommand\s*\(|\bNpgsqlCommand\s+\w+\s*=\s*new\s*\(|\.\s*Where\s*\(\s*\$?"")")]
    private static partial Regex RawSqlCallPattern();

    [GeneratedRegex(@"(?:ExecuteCommand|SqlQuery|GetScalar|GetDataTable|GetInt|GetLong|GetString)\w*(?:<[^>]+>)?\s*\(\s*(?<id>[A-Za-z_][A-Za-z0-9_]*)\b|(?:new\s+NpgsqlCommand|NpgsqlCommand\s+\w+\s*=\s*new)\s*\(\s*(?<id>[A-Za-z_][A-Za-z0-9_]*)\b")]
    private static partial Regex FirstArgIdentifierPattern();

    [GeneratedRegex(@"(?:new\s+NpgsqlCommand|NpgsqlCommand\s+\w+\s*=\s*new)\s*\(\s*(?<id>[a-z][A-Za-z0-9_]*)\s*,")]
    private static partial Regex NpgsqlCommandParamPattern();

    [GeneratedRegex(@"\[SugarTable\(\s*""(?<name>[^""]+)""\s*\)\]")]
    private static partial Regex SugarTablePattern();

    [GeneratedRegex(@"^[a-z0-9_]+$")]
    private static partial Regex TableNamePattern();
}
