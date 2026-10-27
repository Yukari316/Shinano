using Shinano.Host.Restart.Models;

namespace Shinano.Host.Restart.Options;

/// <summary>重启与插件包变更配置，对应配置节 <c>Shinano:Restart</c>，修改后重启生效。</summary>
public sealed class RestartOptions
{
    /// <summary>配置节路径。</summary>
    public const string SECTION_NAME = "Shinano:Restart";

    /// <summary>服务管理器模式下请求服务管理器重启的退出码；服务管理器须把非零退出视为失败并重启。</summary>
    public const int SERVICE_RESTART_EXIT_CODE = 75;

#if DEBUG
    private const string DEFAULT_SERVICE_NAME = "ShinanoDebug";
#else
    private const string DEFAULT_SERVICE_NAME = "Shinano";
#endif

    /// <summary>重启方式，默认 <see cref="RestartMode.Helper" />。</summary>
    public RestartMode Mode { get; set; } = RestartMode.Helper;

    /// <summary>服务名，用于 Windows 服务与 systemd unit；留空时 Debug 构建为 <c>ShinanoDebug</c>，其余为 <c>Shinano</c>。</summary>
    public string? ServiceName { get; set; }

    /// <summary>实际使用的服务名。</summary>
    public string EffectiveServiceName => string.IsNullOrWhiteSpace(ServiceName) ? DEFAULT_SERVICE_NAME : ServiceName.Trim();

    /// <summary>升级成功后是否保留上一套插件包的备份；只保留最近一次。</summary>
    public bool KeepBackup { get; set; } = true;
}
