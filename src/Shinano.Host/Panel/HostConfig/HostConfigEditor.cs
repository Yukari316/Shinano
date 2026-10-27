using System.Text.Json.Nodes;
using Shinano.Host.Configuration;
using Shinano.Host.Panel.HostConfig.Models;
using Shinano.Host.Restart.Models;

namespace Shinano.Host.Panel.HostConfig;

/// <summary>
/// 面板中的宿主配置：读取 <c>appsettings.json</c> 与本地配置文件合并后的值（包括已保存但尚未重启生效的修改），
/// 把修改写入本地配置文件。全部宿主配置只在启动时读取，保存后需要重启生效。敏感项只报告是否已配置。
/// </summary>
public sealed class HostConfigEditor
{
    private const string ROOT_SECTION   = "Shinano";
    private const string APPSETTINGS    = "appsettings.json";
    private const int    MAX_PORT       = 65535;
    private const int    MAX_DAYS       = 3650;

    private static readonly string[] LogLevels    = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];
    private static readonly string[] RestartModes = [nameof(RestartMode.Helper), nameof(RestartMode.ServiceManager)];

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
        Secret("LlBot", "AccessToken", "LLBot AccessToken"),
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
        Secret("Web", "CertificatePassword", "HTTPS 证书密码"),
        Secret("Web", "Token", "面板 Token"),
        Text("Plugins", "Directory", "插件目录", "相对路径基于部署数据目录"),
        Choice("Restart", "Mode", "重启方式", RestartModes, "Helper：直接运行，由重启辅助程序拉起；ServiceManager：由 Windows 服务或 systemd 托管"),
        Text("Restart", "ServiceName", "服务名", "Windows 服务与 systemd unit 的名称；留空时 Debug 构建为 ShinanoDebug，其余为 Shinano", optional: true),
        Boolean("Restart", "KeepBackup", "保留插件包备份", "升级成功后保留上一套插件包，只保留最近一次"),
        Text("Monitor", "ListenAddress", "状态页监听地址", "具体 IP 或 localhost，不接受全部网卡地址；修改后需重启监控进程"),
        Integer("Monitor", "Port", "状态页端口", 1, MAX_PORT, "修改后需重启监控进程"),
        Text("Monitor", "CertificatePath", "状态页 HTTPS 证书路径", "PFX 文件，相对路径基于部署数据目录；留空使用 HTTP", optional: true),
        Secret("Monitor", "CertificatePassword", "状态页 HTTPS 证书密码"),
        Integer("Monitor", "IntervalSeconds", "探测间隔（秒）", 5, 3600, "修改后需重启监控进程"),
        Integer("Monitor", "MaxLogSizeMiB", "监控日志目录上限（MiB）", 16, 1_048_576, "修改后需重启监控进程"),
        Text("Monitor", "ServiceName", "监控服务名", "留空时 Debug 构建为 ShinanoMonitorDebug，其余为 ShinanoMonitor", optional: true)
    ];

    private readonly DeploymentPaths _paths;
    private readonly LocalConfigFile _localConfig;

    /// <summary>创建编辑器。</summary>
    public HostConfigEditor(DeploymentPaths paths, LocalConfigFile localConfig)
    {
        _paths       = paths;
        _localConfig = localConfig;
    }

    /// <summary>读取全部配置项的当前值；敏感项的值为是否已配置。</summary>
    public IReadOnlyList<object> Read()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
                                           .AddJsonFile(Path.Combine(_paths.BaseDirectory, APPSETTINGS), optional: true)
                                           .AddJsonFile(_paths.LocalConfigPath, optional: true)
                                           .Build();
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
    /// 只接受 <see cref="Fields" /> 中的非敏感项；未提交的配置项保持不变。
    /// </summary>
    /// <returns>校验错误；为空时已写入。</returns>
    public IReadOnlyList<string> Save(JsonObject changes)
    {
        List<string> errors = [];
        List<(HostConfigField Field, JsonNode Value)> accepted = [];
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
                if (field is null || field.Kind == HostConfigFieldKind.Secret)
                {
                    errors.Add($"{sectionName}:{key} 不能在面板中修改");
                    continue;
                }

                if (Validate(field, value) is { } error) errors.Add($"{field.Label}：{error}");
                // SAFE: Validate 对 null 值返回错误，通过校验的值非空。
                else accepted.Add((field, value!.DeepClone()));
            }
        }

        if (errors.Count > 0) return errors;

        _localConfig.Update(root =>
        {
            foreach ((HostConfigField field, JsonNode value) in accepted)
                LocalConfigFile.Section(root, ROOT_SECTION, field.Section)[field.Key] = value;
        });
        return [];
    }

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

    private static HostConfigField Secret(string section, string key, string label) =>
        new()
        {
            Section = section,
            Key     = key,
            Label   = label,
            Hint    = "敏感配置只能在本地配置文件中修改",
            Kind    = HostConfigFieldKind.Secret
        };
}
