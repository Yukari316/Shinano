using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using Npgsql;
using Shinano.Tool.Cli;
using Shinano.Tool.Configuration;
using Shinano.Tool.Deployment;
using Shinano.Tool.Plugins;
using Shinano.Tool.Run;
using Shinano.Tool.Services;

namespace Shinano.Tool.Setup;

/// <summary>
/// <c>init</c>：逐项询问部署配置并写入本地配置。重复运行时以现有值为默认值，不覆盖未修改项与已生成的 Token。
/// 首次运行时装入随包原生插件（不启用）；服务模式下创建并启动服务。未问到的配置项保持默认，可在面板修改。
/// </summary>
public static class InitCommand
{
    /// <summary>不询问是否立即启动；由 <c>start</c> 脚本在首次运行时使用。</summary>
    public const string NO_START_SWITCH = "no-start";

    private const string RUN_MODE_OPTION             = "run-mode";
    private const string HOST_SERVICE_OPTION         = "host-service-name";
    private const string MONITOR_SERVICE_OPTION      = "monitor-service-name";
    private const string SERVICE_USER_OPTION         = "service-user";
    private const string TIME_ZONE_OPTION            = "timezone";
    private const string BUCKET_PREFIX_OPTION        = "bucket-prefix";
    private const string SUPER_USERS_OPTION          = "super-users";
    private const string DB_HOST_OPTION              = "db-host";
    private const string DB_PORT_OPTION              = "db-port";
    private const string DB_NAME_OPTION              = "db-name";
    private const string DB_USER_OPTION              = "db-user";
    private const string DB_ADMIN_USER_OPTION        = "db-admin-user";
    private const string RUSTFS_ENDPOINT_OPTION      = "rustfs-endpoint";
    private const string RUSTFS_IM_ENDPOINT_OPTION   = "rustfs-im-endpoint";
    private const string LLBOT_HOST_OPTION           = "llbot-host";
    private const string LLBOT_PORT_OPTION           = "llbot-port";
    private const string LLBOT_PREFIX_OPTION         = "llbot-prefix";
    private const string PANEL_LISTEN_OPTION         = "panel-listen";
    private const string PANEL_PORT_OPTION           = "panel-port";
    private const string STATUS_LISTEN_OPTION        = "status-listen";
    private const string STATUS_PORT_OPTION          = "status-port";
    private const string CERT_PATH_OPTION            = "cert-path";
    private const string STATUS_SAME_CERT_OPTION     = "status-same-cert";
    private const string STATUS_CERT_PATH_OPTION     = "status-cert-path";
    private const string NB2_SETUP_OPTION            = "nb2-setup";
    private const string DB_PASSWORD_VARIABLE        = "SHINANO_INIT_DB_PASSWORD";
    private const string DB_ADMIN_PASSWORD_VARIABLE  = "SHINANO_INIT_DB_ADMIN_PASSWORD";
    private const string RUSTFS_ACCESS_KEY_VARIABLE  = "SHINANO_INIT_RUSTFS_ACCESS_KEY";
    private const string RUSTFS_SECRET_KEY_VARIABLE  = "SHINANO_INIT_RUSTFS_SECRET_KEY";
    private const string LLBOT_TOKEN_VARIABLE        = "SHINANO_INIT_LLBOT_TOKEN";
    private const string CERT_PASSWORD_VARIABLE      = "SHINANO_INIT_CERT_PASSWORD";
    private const string STATUS_CERT_PASSWORD_VARIABLE = "SHINANO_INIT_STATUS_CERT_PASSWORD";
    private const string DIRECT                      = "direct";
    private const string SERVICE                     = "service";
    private const string DEDICATED_USER              = "shinano";
    private const string DEFAULT_TIME_ZONE           = "Asia/Shanghai";
    private const string DEFAULT_BUCKET_PREFIX       = "shinano";
    private const string DEFAULT_LOOPBACK            = "127.0.0.1";
    private const string DEFAULT_DATABASE            = "shinano";
    private const string DEFAULT_ROLE                = "shinano";
    private const string DEFAULT_ADMIN               = "postgres";
    private const int    DEFAULT_LLBOT_PORT          = 3000;

    /// <summary>该命令接受的选项。</summary>
    public static readonly IReadOnlyCollection<string> Options =
    [
        RUN_MODE_OPTION, HOST_SERVICE_OPTION, MONITOR_SERVICE_OPTION, SERVICE_USER_OPTION, TIME_ZONE_OPTION, BUCKET_PREFIX_OPTION,
        SUPER_USERS_OPTION, DB_HOST_OPTION, DB_PORT_OPTION, DB_NAME_OPTION, DB_USER_OPTION, DB_ADMIN_USER_OPTION, RUSTFS_ENDPOINT_OPTION,
        RUSTFS_IM_ENDPOINT_OPTION, LLBOT_HOST_OPTION, LLBOT_PORT_OPTION, LLBOT_PREFIX_OPTION, PANEL_LISTEN_OPTION, PANEL_PORT_OPTION,
        STATUS_LISTEN_OPTION, STATUS_PORT_OPTION, CERT_PATH_OPTION, STATUS_SAME_CERT_OPTION, STATUS_CERT_PATH_OPTION, NB2_SETUP_OPTION,
        ToolContext.ACCEPT_DEFAULTS_SWITCH, NO_START_SWITCH
    ];

    /// <summary>运行初始化。</summary>
    public static async Task<int> RunAsync(ToolContext context)
    {
        DeploymentLayout layout = context.Layout;
        Prompter ask = context.Prompter;
        ToolLog log = context.Log;
        if (!File.Exists(layout.HostExecutable))
            throw new ToolException($"找不到宿主程序 {layout.HostExecutable}；请在解压后的发布包中运行 scripts 下的 init");

        LocalConfig config = LocalConfig.Load(layout.LocalConfigPath);
        DeploymentState? previous = DeploymentStateStore.Read(layout);
        DeploymentState state = previous ?? new DeploymentState();
        RunMode previousMode = state.RunMode;
        string previousHostService = state.HostServiceName;
        string previousMonitorService = state.MonitorServiceName;
        bool previouslyInstalled = state.ServicesInstalled;

        log.Say(previous is null ? "Shinano 初始化。直接回车使用方括号中的默认值。" : "重新配置 Shinano。直接回车保持方括号中的现有值。");
        AskRunMode(ask, state);
        AskDeployment(ask, config);
        await AskDatabaseAsync(context, config);
        AskRustFs(ask, config);
        AskLlBot(ask, config);
        AskListen(ask, config, layout.DataDirectory, log);

        bool newToken = !PanelToken.IsValid(config.GetString(ConfigKeys.PANEL_TOKEN));
        string? token = newToken ? PanelToken.Generate() : null;
        if (token is not null) config.Set(ConfigKeys.PANEL_TOKEN, token);
        config.Set(ConfigKeys.HOST_SERVICE_NAME, state.HostServiceName);
        config.Set(ConfigKeys.MONITOR_SERVICE_NAME, state.MonitorServiceName);
        config.Save();
        log.Info($"已写入本地配置 {layout.LocalConfigPath}");

        if (previous is null)
        {
            string pluginRoot = Path.GetFullPath(
                config.GetString(ConfigKeys.PLUGIN_DIRECTORY) ?? ConfigKeys.DEFAULT_PLUGIN_DIRECTORY,
                layout.DataDirectory);
            BundledPluginSync.InstallInitial(PluginPackages.ReadBundled(layout.BundledPluginsDirectory), pluginRoot, state, log);
        }

        if (ask.Confirm("是否现在创建 NB2 运行环境（需要已安装 uv，会下载 Python 与依赖）", NB2_SETUP_OPTION, fallback: false))
        {
            try
            {
                Nb2SetupCommand.Setup(Path.Combine(layout.DataDirectory, Nb2SetupCommand.DIRECTORY_NAME), log);
            }
            catch (ToolException exception)
            {
                // NB2 是可选功能，失败不影响其余初始化。
                log.Error($"创建 NB2 运行环境失败：{exception.Message}；稍后可运行 scripts 下的 nb2-setup 重试");
            }
        }

        DeploymentStateStore.Write(layout, state);
        await ApplyRunModeAsync(context, state, previousMode, previousHostService, previousMonitorService, previouslyInstalled);

        if (token is not null)
        {
            log.Say(string.Empty);
            log.Say("面板登录 Token（只显示这一次，请妥善保存）：");
            log.Say($"  {token}");
            log.Say(string.Empty);
        }

        if (state.RunMode == RunMode.Service) return 0;
        if (!context.Arguments.Switch(NO_START_SWITCH) && ask.Interactive && ask.Confirm("是否立即启动", null, fallback: true))
            return await Supervisor.RunAsync(context);
        log.Say("运行 scripts 下的 start 启动 Shinano。");
        return 0;
    }

    private static void AskRunMode(Prompter ask, DeploymentState state)
    {
        string mode = ask.Text(
            "运行方式（direct：直接运行；service：作为系统服务）",
            RUN_MODE_OPTION,
            state.RunMode == RunMode.Service ? SERVICE : DIRECT,
            value => value is DIRECT or SERVICE ? null : "请输入 direct 或 service");
        state.RunMode = mode == SERVICE ? RunMode.Service : RunMode.Direct;
        if (state.RunMode != RunMode.Service) return;

        state.HostServiceName = ask.Text("宿主服务名", HOST_SERVICE_OPTION, state.HostServiceName, InputRules.ServiceName);
        state.MonitorServiceName = ask.Text("状态页服务名", MONITOR_SERVICE_OPTION, state.MonitorServiceName, InputRules.ServiceName);
        if (state.HostServiceName.Equals(state.MonitorServiceName, StringComparison.OrdinalIgnoreCase))
            throw new ToolException("宿主与状态页的服务名不能相同");
        if (!OperatingSystem.IsLinux()) return;

        bool dedicated = ask.Confirm($"是否以专用用户 {DEDICATED_USER} 运行服务（不存在时创建，实现账户隔离）", SERVICE_USER_OPTION, fallback: true);
        if (dedicated) state.ServiceUser = DEDICATED_USER;
        else
        {
            state.ServiceUser = null;
            state.ServiceUserCreated = false;
        }
    }

    private static void AskDeployment(Prompter ask, LocalConfig config)
    {
        config.Set(
            ConfigKeys.TIME_ZONE,
            ask.Text("时区", TIME_ZONE_OPTION, config.GetString(ConfigKeys.TIME_ZONE) ?? DEFAULT_TIME_ZONE, InputRules.TimeZone));
        config.Set(
            ConfigKeys.BUCKET_PREFIX,
            ask.Text("bucket 前缀（每个部署唯一）", BUCKET_PREFIX_OPTION, config.GetString(ConfigKeys.BUCKET_PREFIX) ?? DEFAULT_BUCKET_PREFIX, InputRules.BucketPrefix));

        long[] current = config.GetLongArray(ConfigKeys.SUPER_USERS);
        string superUsers = ask.Text(
            "Super User 的 QQ 号（多个用逗号分隔）",
            SUPER_USERS_OPTION,
            current.Length > 0 ? string.Join(',', current) : null,
            InputRules.SuperUsers);
        // SAFE: 校验已保证可以解析。
        config.Set(ConfigKeys.SUPER_USERS, new JsonArray([.. InputRules.ParseIds(superUsers)!.Select(id => (JsonNode)id)]));
    }

    private static async Task AskDatabaseAsync(ToolContext context, LocalConfig config)
    {
        Prompter ask = context.Prompter;
        string? existing = config.GetString(ConfigKeys.DATABASE_DSN);
        NpgsqlConnectionStringBuilder original = DsnFormat.Parse(existing);
        NpgsqlConnectionStringBuilder dsn = DsnFormat.Parse(existing);
        dsn.Host = ask.Text("PostgreSQL 主机", DB_HOST_OPTION, dsn.Host ?? DEFAULT_LOOPBACK, InputRules.HostName);
        dsn.Port = int.Parse(ask.Text("PostgreSQL 端口", DB_PORT_OPTION, dsn.Port.ToString(), InputRules.Port));
        dsn.Database = ask.Text("数据库名", DB_NAME_OPTION, dsn.Database ?? DEFAULT_DATABASE, InputRules.Identifier);
        dsn.Username = ask.Text("业务角色（Bot 日常使用，不能是超级用户）", DB_USER_OPTION, dsn.Username ?? DEFAULT_ROLE, InputRules.Identifier);
        if (ask.Secret("业务角色密码", DB_PASSWORD_VARIABLE, dsn.Password is { Length: > 0 }, required: true) is { } password)
            dsn.Password = password;

        bool provision = context.Arguments.Value(DB_ADMIN_USER_OPTION) is not null
                         || ask.Confirm("是否用管理员账号创建数据库与业务角色（已由 DBA 建好可跳过）", null, fallback: false);
        if (provision)
        {
            string adminUser = ask.Text("管理员用户名", DB_ADMIN_USER_OPTION, DEFAULT_ADMIN, InputRules.Identifier);
            // SAFE: required 为 true 且没有现值时，Secret 要么返回输入值，要么抛出异常。
            string adminPassword = ask.Secret("管理员密码（只在本次使用，不保存）", DB_ADMIN_PASSWORD_VARIABLE, configured: false, required: true)!;
            await DatabaseSetup.ProvisionAsync(dsn, adminUser, adminPassword, context.Log);
        }

        if (await DatabaseSetup.VerifyAsync(dsn) is { } problem)
        {
            context.Log.Warning(problem);
            if (!ask.Confirm("无法连接数据库，仍然保存配置吗", null, fallback: false))
                throw new ToolException("已取消，配置未保存；请检查数据库后重新运行 init");
        }
        else
        {
            context.Log.Say("  数据库连接与前提核验通过");
        }

        // 连接信息未改动时保留原 DSN 文本（可能是 postgres:// URI 形式）。
        if (existing is null || !DsnFormat.SameTarget(original, dsn)) config.Set(ConfigKeys.DATABASE_DSN, dsn.ConnectionString);
    }

    private static void AskRustFs(Prompter ask, LocalConfig config)
    {
        string endpoint = ask.Text("RustFS 内部地址（如 http://10.0.0.2:9000）", RUSTFS_ENDPOINT_OPTION, config.GetString(ConfigKeys.RUSTFS_ENDPOINT), InputRules.HttpUrl);
        config.Set(ConfigKeys.RUSTFS_ENDPOINT, endpoint);
        string imEndpoint = ask.Text(
            "RustFS 的 IM 可达地址（留空与内部地址相同）",
            RUSTFS_IM_ENDPOINT_OPTION,
            config.GetString(ConfigKeys.RUSTFS_IM_ENDPOINT) ?? string.Empty,
            value => value.Length == 0 ? null : InputRules.HttpUrl(value));
        if (imEndpoint.Length == 0) config.Remove(ConfigKeys.RUSTFS_IM_ENDPOINT);
        else config.Set(ConfigKeys.RUSTFS_IM_ENDPOINT, imEndpoint);

        SetSecret(ask, config, ConfigKeys.RUSTFS_ACCESS_KEY, "RustFS AccessKey", RUSTFS_ACCESS_KEY_VARIABLE, required: true);
        SetSecret(ask, config, ConfigKeys.RUSTFS_SECRET_KEY, "RustFS SecretKey", RUSTFS_SECRET_KEY_VARIABLE, required: true);
    }

    private static void AskLlBot(Prompter ask, LocalConfig config)
    {
        config.Set(ConfigKeys.LLBOT_HOST, ask.Text("LLBot 主机", LLBOT_HOST_OPTION, config.GetString(ConfigKeys.LLBOT_HOST) ?? DEFAULT_LOOPBACK, InputRules.HostName));
        config.Set(
            ConfigKeys.LLBOT_PORT,
            int.Parse(ask.Text("LLBot 端口", LLBOT_PORT_OPTION, (config.GetInt(ConfigKeys.LLBOT_PORT) ?? DEFAULT_LLBOT_PORT).ToString(), InputRules.Port)));
        config.Set(ConfigKeys.LLBOT_PREFIX, ask.Text("Milky 路径前缀（没有可留空）", LLBOT_PREFIX_OPTION, config.GetString(ConfigKeys.LLBOT_PREFIX) ?? string.Empty));
        SetSecret(ask, config, ConfigKeys.LLBOT_ACCESS_TOKEN, "LLBot AccessToken（没有可留空）", LLBOT_TOKEN_VARIABLE, required: false);
    }

    private static void AskListen(Prompter ask, LocalConfig config, string dataDirectory, ToolLog log)
    {
        AskEndpoint(ask, config, ConfigKeys.WEB, "面板", PANEL_LISTEN_OPTION, PANEL_PORT_OPTION, ConfigKeys.DEFAULT_PANEL_PORT);
        AskEndpoint(ask, config, ConfigKeys.MONITOR, "状态页", STATUS_LISTEN_OPTION, STATUS_PORT_OPTION, ConfigKeys.DEFAULT_STATUS_PORT);

        string certificate = AskCertificate(ask, config, ConfigKeys.WEB, "面板", CERT_PATH_OPTION, CERT_PASSWORD_VARIABLE, dataDirectory, log);
        if (certificate.Length > 0 && ask.Confirm("状态页使用与面板相同的证书", STATUS_SAME_CERT_OPTION, fallback: true))
        {
            config.Set(ConfigKeys.In(ConfigKeys.MONITOR, ConfigKeys.CERTIFICATE_PATH), certificate);
            string? password = config.GetString(ConfigKeys.In(ConfigKeys.WEB, ConfigKeys.CERTIFICATE_PASSWORD));
            if (password is null) config.Remove(ConfigKeys.In(ConfigKeys.MONITOR, ConfigKeys.CERTIFICATE_PASSWORD));
            else config.Set(ConfigKeys.In(ConfigKeys.MONITOR, ConfigKeys.CERTIFICATE_PASSWORD), password);
            return;
        }

        AskCertificate(ask, config, ConfigKeys.MONITOR, "状态页", STATUS_CERT_PATH_OPTION, STATUS_CERT_PASSWORD_VARIABLE, dataDirectory, log);
    }

    private static void AskEndpoint(Prompter ask, LocalConfig config, string section, string label, string listenOption, string portOption, int defaultPort)
    {
        string addressKey = ConfigKeys.In(section, ConfigKeys.LISTEN_ADDRESS);
        string portKey = ConfigKeys.In(section, ConfigKeys.PORT);
        config.Set(
            addressKey,
            ask.Text($"{label}监听地址（127.0.0.1 只允许本机访问，局域网访问请填本机局域网 IP）", listenOption, config.GetString(addressKey) ?? DEFAULT_LOOPBACK, InputRules.ListenAddress));
        config.Set(portKey, int.Parse(ask.Text($"{label}端口", portOption, (config.GetInt(portKey) ?? defaultPort).ToString(), InputRules.Port)));
    }

    // 返回证书路径；不启用 HTTPS 时为空字符串。证书无法加载时宿主只在 127.0.0.1 用 HTTP，这里提前告警。
    private static string AskCertificate(
        Prompter    ask,
        LocalConfig config,
        string      section,
        string      label,
        string      option,
        string      passwordVariable,
        string      dataDirectory,
        ToolLog     log)
    {
        string pathKey = ConfigKeys.In(section, ConfigKeys.CERTIFICATE_PATH);
        string passwordKey = ConfigKeys.In(section, ConfigKeys.CERTIFICATE_PASSWORD);
        string path = ask.Text($"{label} HTTPS 证书（PFX，相对部署数据目录；留空使用 HTTP）", option, config.GetString(pathKey) ?? string.Empty);
        if (path.Length == 0)
        {
            config.Remove(pathKey);
            config.Remove(passwordKey);
            return path;
        }

        config.Set(pathKey, path);
        SetSecret(ask, config, passwordKey, $"{label}证书密码（没有可留空）", passwordVariable, required: false);
        string fullPath = Path.GetFullPath(path, dataDirectory);
        try
        {
            using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(fullPath, config.GetString(passwordKey));
            if (!certificate.HasPrivateKey) log.Warning($"证书 {fullPath} 不含私钥，{label}将只在 127.0.0.1 以 HTTP 监听");
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            log.Warning($"无法加载证书 {fullPath}：{exception.Message}；{label}将只在 127.0.0.1 以 HTTP 监听");
        }

        return path;
    }

    private static void SetSecret(Prompter ask, LocalConfig config, string key, string label, string variable, bool required)
    {
        if (ask.Secret(label, variable, config.GetString(key) is not null, required) is { } value) config.Set(key, value);
    }

    private static async Task ApplyRunModeAsync(
        ToolContext     context,
        DeploymentState state,
        RunMode         previousMode,
        string          previousHostService,
        string          previousMonitorService,
        bool            previouslyInstalled)
    {
        bool sameServices = previousMode == RunMode.Service
                            && previousHostService == state.HostServiceName
                            && previousMonitorService == state.MonitorServiceName;
        if (previouslyInstalled && !(state.RunMode == RunMode.Service && sameServices))
        {
            context.Log.Say("运行方式或服务名已改变，先删除原有服务。");
            DeploymentState old = new()
            {
                RunMode            = RunMode.Service,
                HostServiceName    = previousHostService,
                MonitorServiceName = previousMonitorService,
                ServicesInstalled  = true,
                ServiceUser        = state.ServiceUser,
                ServiceUserCreated = false,
                BundledPlugins     = state.BundledPlugins
            };
            IServiceManager previousManager = ServiceManagers.ForCurrentPlatform();
            if (!await ServiceCommand.UninstallAsync(context, old, previousManager, ServiceManagers.Setup(context.Layout, old), removingBot: false))
                throw new ToolException("原有服务未能删除；请按上面的命令处理后重新运行 init");
            state.ServicesInstalled = false;
            DeploymentStateStore.Write(context.Layout, state);
        }

        if (state.RunMode != RunMode.Service) return;
        if (state.ServicesInstalled)
        {
            context.Log.Say("服务已存在。配置修改在重启服务后生效：先运行 service stop，再运行 service start。");
            return;
        }

        await ServiceCommand.InstallAsync(context, state, ServiceManagers.ForCurrentPlatform());
    }
}
