using Shinano.Host.Bot.Models;
using Shinano.Host.Configuration;
using Shinano.Host.Panel.Models;
using Shinano.Host.Rendering.Models;
using Shinano.Infrastructure.Storage.Models;
using System.Text.Json.Nodes;

namespace Shinano.Host.Panel;

/// <summary>
/// 面板中的宿主配置：读取 <c>appsettings.json</c> 与本地配置文件合并后的值（包括已保存但尚未重启生效的修改），
/// 把修改写入本地配置文件。全部宿主配置只在启动时读取，保存后需要重启生效。
/// 敏感项只写：读取时只报告是否已配置，保存时接受新值，不回显。面板 Token 由重新生成接口单独管理，不在此列出。
/// </summary>
public sealed class HostConfigEditor
{
    private const string ROOT_SECTION   = "Shinano";
    private const string APPSETTINGS    = "appsettings.json";
    private const int    MAX_PORT       = 65535;
    private const int    MAX_DAYS       = 3650;
    private const string DATABASE       = "Database";
    private const string LLBOT          = "LlBot";
    private const string RUSTFS         = "RustFs";
    private const string WEB            = "Web";
    private const string MONITOR        = "Monitor";
    private const string CERTIFICATE    = "CertificatePath";
    private const string CERTIFICATE_PW = "CertificatePassword";

    private static readonly string[] RustFsConnectionKeys = ["Endpoint", "ImEndpoint", "AccessKey", "SecretKey"];

    private static readonly string[] LogLevels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];

    /// <summary>面板显示的全部宿主配置项，按显示顺序排列。</summary>
    public static readonly IReadOnlyList<HostConfigField> Fields =
    [
        Text("Deployment", "TimeZone", "时区", "IANA 或 Windows 时区 ID，决定聊天分区与统计的自然日"),
        Text("Deployment", "BucketPrefix", "Bucket 前缀", "每个部署必须唯一；修改后原有媒体不会迁移"),
        IntegerList("Deployment", "SuperUsers", "Super User", "QQ 号列表"),
        Secret("Database", "Dsn", "数据库 DSN"),
        Text("LlBot", "Host", "LLBot 地址"),
        Integer("LlBot", "Port", "LLBot 端口", 1, MAX_PORT),
        Text("LlBot", "Prefix", "LLBot 路径前缀", optional: true),
        Secret("LlBot", "AccessToken", "LLBot AccessToken", optional: true),
        Text("RustFs", "Endpoint", "RustFS 内部地址"),
        Text("RustFs", "ImEndpoint", "RustFS IM 可达地址", "留空时与内部地址相同", optional: true),
        Secret("RustFs", "AccessKey", "RustFS AccessKey"),
        Secret("RustFs", "SecretKey", "RustFS SecretKey"),
        Integer("RustFs", "PresignExpiryMinutes", "预签名链接有效期（分钟）", 1, 7 * 24 * 60),
        Integer("Media", "MaxFileSizeMiB", "单文件上限（MiB）", 1, 4096),
        Integer("Media", "OrphanGraceHours", "持久媒体回收宽限期（小时）", 0, 24 * 365),
        Integer("ChatArchive", "RetentionDays", "聊天记录保留天数", 1, MAX_DAYS),
        Integer("ChatArchive", "PartitionDaysAhead", "提前创建分区天数", 1, 30),
        Integer("ChatArchive", "BufferCapacity", "归档缓冲容量（条）", 100, 1_000_000),
        Integer("ChatArchive", "StatRetentionDays", "发言统计保留天数", 0, MAX_DAYS, "0 表示永久保存"),
        Choice("Logging", "MinimumLevel", "日志等级", LogLevels),
        Integer("Logging", "MaxTotalSizeMiB", "日志目录上限（MiB）", 64, 1_048_576),
        Text("Web", "ListenAddress", "面板监听地址", "具体 IP 或 localhost，不接受全部网卡地址"),
        Integer("Web", "Port", "面板端口", 1, MAX_PORT),
        Text("Web", "CertificatePath", "HTTPS 证书路径", "PFX 文件，相对路径基于部署数据目录；留空使用 HTTP", optional: true),
        Secret("Web", "CertificatePassword", "HTTPS 证书密码", optional: true),
        Integer("Rendering", "Width", "消息图片宽度（像素）", RenderingOptions.MIN_WIDTH, RenderingOptions.MAX_WIDTH, "通知、列表等消息图片的输出宽度，高度随内容增长"),
        Text("Plugins", "Directory", "插件目录", "相对路径基于部署数据目录"),
        Text("Restart", "ServiceName", "服务名", "Windows 服务与 systemd unit 的名称；留空时 Debug 构建为 ShinanoDebug，其余为 Shinano", optional: true),
        Boolean("Restart", "KeepBackup", "保留插件包备份", "升级成功后保留上一套插件包，只保留最近一次"),
        Text("Monitor", "ListenAddress", "状态页监听地址", "具体 IP 或 localhost，不接受全部网卡地址；修改后需重启监控进程"),
        Integer("Monitor", "Port", "状态页端口", 1, MAX_PORT, "修改后需重启监控进程"),
        Text("Monitor", "CertificatePath", "状态页 HTTPS 证书路径", "PFX 文件，相对路径基于部署数据目录；留空使用 HTTP", optional: true),
        Secret("Monitor", "CertificatePassword", "状态页 HTTPS 证书密码", optional: true),
        Integer("Monitor", "IntervalSeconds", "探测间隔（秒）", 5, 3600, "修改后需重启监控进程"),
        Integer("Monitor", "MaxLogSizeMiB", "监控日志目录上限（MiB）", 16, 1_048_576, "修改后需重启监控进程"),
        Text("Monitor", "ServiceName", "监控服务名", "留空时 Debug 构建为 ShinanoMonitorDebug，其余为 ShinanoMonitor", optional: true)
    ];

    private readonly DeploymentPaths           _paths;
    private readonly LocalConfigFile           _localConfig;
    private readonly HostConfigConnectionCheck _check;
    private readonly ILogger<HostConfigEditor> _logger;

    /// <summary>创建编辑器。</summary>
    public HostConfigEditor(
        DeploymentPaths           paths,
        LocalConfigFile           localConfig,
        HostConfigConnectionCheck check,
        ILogger<HostConfigEditor> logger)
    {
        _paths       = paths;
        _localConfig = localConfig;
        _check       = check;
        _logger      = logger;
    }

    /// <summary>读取全部配置项的当前值；敏感项的值为是否已配置。</summary>
    public IReadOnlyList<object> Read()
    {
        IConfigurationRoot configuration = LoadConfiguration([]);
        return
        [
            .. Fields.Select(field =>
            {
                IConfigurationSection section = configuration.GetSection($"{ROOT_SECTION}:{field.Section}:{field.Key}");
                object? value = field.Kind switch
                {
                    HostConfigFieldKind.Secret      => !string.IsNullOrEmpty(section.Value),
                    HostConfigFieldKind.IntegerList => section.GetChildren()
                                                               .Select(child => long.TryParse(child.Value, out long id) ? id : 0)
                                                               .ToArray(),
                    HostConfigFieldKind.Integer     => long.TryParse(section.Value, out long number) ? number : null,
                    HostConfigFieldKind.Boolean     => bool.TryParse(section.Value, out bool flag) && flag,
                    _                               => section.Value ?? string.Empty
                };
                return (object)new
                {
                    field,
                    value
                };
            })
        ];
    }

    /// <summary>
    /// 校验并写入修改。<paramref name="changes" /> 形如 <c>{ "Deployment": { "TimeZone": "Asia/Shanghai" } }</c>，
    /// 只接受 <see cref="Fields" /> 中的配置项；未提交的配置项保持不变。敏感项提交新的字符串值，
    /// 可选的敏感项提交 <c>null</c> 表示清除。修改涉及数据库、LLBot、RustFS 或 HTTPS 证书时，
    /// 先用修改后的整组配置实际连接或加载，全部通过才写入。
    /// </summary>
    public async Task<HostConfigSaveResult> SaveAsync(JsonObject changes, CancellationToken cancellationToken)
    {
        List<string> errors = [];
        List<(HostConfigField Field, JsonNode? Value)> accepted = [];
        foreach ((string sectionName, JsonNode? sectionNode) in changes)
        {
            if (sectionNode is not JsonObject section)
            {
                errors.Add($"{sectionName} 必须是对象");
                continue;
            }

            foreach ((string key, JsonNode? value) in section)
            {
                HostConfigField? field = Fields.FirstOrDefault(item => item.Section == sectionName && item.Key == key);
                if (field is null) errors.Add($"{sectionName}:{key} 不能在面板中修改");
                else if (Validate(field, value) is { } error) errors.Add($"{field.Label}：{error}");
                else accepted.Add((field, value?.DeepClone()));
            }
        }

        if (errors.Count > 0) return new HostConfigSaveResult { Errors = errors };

        List<string> notes = [];
        foreach (ConnectionCheck check in await CheckConnectionsAsync(accepted, cancellationToken))
        {
            if (!check.Succeeded) errors.Add(check.Message);
            else if (check.Message.Length > 0) notes.Add(check.Message);
        }

        if (errors.Count > 0)
        {
            _logger.LogWarning("宿主配置未保存，连接检查未通过：{Errors}", string.Join("；", errors));
            return new HostConfigSaveResult { Errors = errors };
        }

        _localConfig.Update(root =>
        {
            foreach ((HostConfigField field, JsonNode? value) in accepted)
            {
                JsonObject section = LocalConfigFile.Section(root, ROOT_SECTION, field.Section);
                if (value is null) section.Remove(field.Key);
                else section[field.Key] = value;
            }
        });
        // 只记录配置项名称，不记录值。
        _logger.LogInformation(
            "面板保存了宿主配置：{Keys}",
            string.Join("、", accepted.Select(item => $"{item.Field.Section}:{item.Field.Key}")));
        return new HostConfigSaveResult { Notes = notes };
    }

    // 按修改涉及的配置组检查；用于检查的是叠加了本次修改后的完整配置。
    private async Task<IReadOnlyList<ConnectionCheck>> CheckConnectionsAsync(
        IReadOnlyList<(HostConfigField Field, JsonNode? Value)> accepted,
        CancellationToken                                         cancellationToken)
    {
        bool Touched(string section, params string[] keys) =>
            accepted.Any(item => item.Field.Section == section && (keys.Length == 0 || keys.Contains(item.Field.Key)));

        IConfigurationRoot configuration = LoadConfiguration(accepted);
        string? Value(string section, string key) => configuration[$"{ROOT_SECTION}:{section}:{key}"];

        List<ConnectionCheck> checks = [];
        if (Touched(DATABASE))
            checks.Add(await _check.CheckDatabaseAsync(Value(DATABASE, "Dsn") ?? string.Empty, cancellationToken));
        if (Touched(LLBOT))
        {
            LlBotOptions llBot = configuration.GetSection(LlBotOptions.SECTION_NAME).Get<LlBotOptions>() ?? new LlBotOptions();
            checks.Add(await _check.CheckLlBotAsync(llBot.Host, llBot.Port, llBot.Prefix, llBot.AccessToken, cancellationToken));
        }

        if (Touched(RUSTFS, RustFsConnectionKeys))
            checks.Add(await _check.CheckRustFsAsync(
                configuration.GetSection(RustFsOptions.SECTION_NAME).Get<RustFsOptions>() ?? new RustFsOptions(),
                cancellationToken));
        foreach (string section in (string[])[WEB, MONITOR])
        {
            if (Touched(section, CERTIFICATE, CERTIFICATE_PW) && Value(section, CERTIFICATE) is { Length: > 0 } path)
                checks.Add(_check.CheckCertificate(path, Value(section, CERTIFICATE_PW)));
        }

        return checks;
    }

    // appsettings.json 与本地配置文件合并，再叠加尚未写入的修改（整数列表不参与连接检查，不叠加）。
    private IConfigurationRoot LoadConfiguration(IEnumerable<(HostConfigField Field, JsonNode? Value)> overrides) =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(_paths.BaseDirectory, APPSETTINGS), optional: true)
            .AddJsonFile(_paths.LocalConfigPath, optional: true)
            .AddInMemoryCollection(
                overrides.Where(item => item.Value is not JsonArray)
                         .Select(item => new KeyValuePair<string, string?>(
                             $"{ROOT_SECTION}:{item.Field.Section}:{item.Field.Key}",
                             item.Value?.ToString())))
            .Build();

    private static string? Validate(HostConfigField field, JsonNode? value)
    {
        switch (field.Kind)
        {
            case HostConfigFieldKind.Text when value is JsonValue text && text.TryGetValue(out string? content):
                if (content.Length == 0 && !field.Optional) return "不能为空";
                if (field is { Section: "Deployment", Key: "TimeZone" } && !TimeZoneInfo.TryFindSystemTimeZoneById(content, out _))
                    return $"找不到时区 {content}";
                return null;
            case HostConfigFieldKind.Integer when value is JsonValue number && number.TryGetValue(out long integer):
                return integer < field.Minimum || integer > field.Maximum ? $"必须在 {field.Minimum} 到 {field.Maximum} 之间" : null;
            case HostConfigFieldKind.IntegerList when value is JsonArray items:
                return items.All(item => item is JsonValue element && element.TryGetValue(out long _)) ? null : "必须是整数列表";
            case HostConfigFieldKind.Choice when value is JsonValue choice && choice.TryGetValue(out string? option):
                return field.Options.Contains(option) ? null : $"必须是 {string.Join("、", field.Options)} 之一";
            case HostConfigFieldKind.Boolean when value is JsonValue flag && flag.TryGetValue(out bool _):
                return null;
            case HostConfigFieldKind.Secret when value is null:
                return field.Optional ? null : "不能清除";
            case HostConfigFieldKind.Secret when value is JsonValue secret && secret.TryGetValue(out string? content):
                return content.Length == 0 ? "不能为空" : null;
            default:
                return "值的类型不正确";
        }
    }

    private static HostConfigField Text(string section, string key, string label, string hint = "", bool optional = false) =>
        new()
        {
            Section  = section,
            Key      = key,
            Label    = label,
            Hint     = hint,
            Kind     = HostConfigFieldKind.Text,
            Optional = optional
        };

    private static HostConfigField Integer(string section, string key, string label, long minimum, long maximum, string hint = "") =>
        new()
        {
            Section = section,
            Key     = key,
            Label   = label,
            Hint    = hint,
            Kind    = HostConfigFieldKind.Integer,
            Minimum = minimum,
            Maximum = maximum
        };

    private static HostConfigField IntegerList(string section, string key, string label, string hint) =>
        new()
        {
            Section = section,
            Key     = key,
            Label   = label,
            Hint    = hint,
            Kind    = HostConfigFieldKind.IntegerList
        };

    private static HostConfigField Choice(string section, string key, string label, IReadOnlyList<string> options, string hint = "") =>
        new()
        {
            Section = section,
            Key     = key,
            Label   = label,
            Hint    = hint,
            Kind    = HostConfigFieldKind.Choice,
            Options = options
        };

    private static HostConfigField Boolean(string section, string key, string label, string hint) =>
        new()
        {
            Section = section,
            Key     = key,
            Label   = label,
            Hint    = hint,
            Kind    = HostConfigFieldKind.Boolean
        };

    private static HostConfigField Secret(string section, string key, string label, bool optional = false) =>
        new()
        {
            Section  = section,
            Key      = key,
            Label    = label,
            Hint     = "已保存的值不会显示；输入新值即替换",
            Kind     = HostConfigFieldKind.Secret,
            Optional = optional
        };
}
