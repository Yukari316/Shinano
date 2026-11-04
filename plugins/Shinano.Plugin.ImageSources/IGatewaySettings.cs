namespace Shinano.Plugin.ImageSources;

/// <summary>
/// 图源服务的地址与令牌，由使用共享库的插件从各自的配置与机密提供；每次调用时读取，面板修改后立即生效。
/// </summary>
public interface IGatewaySettings
{
    /// <summary>图源服务地址，如 <c>https://api.kosaka-wakamo.top/</c>。</summary>
    string BaseUrl { get; }

    /// <summary>访问令牌；未配置时为 null。</summary>
    string? Token { get; }
}
