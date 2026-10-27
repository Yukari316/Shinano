namespace Shinano.Host.Panel.Models;

/// <summary>登录请求。</summary>
public sealed class LoginRequest
{
    /// <summary>面板 Token。</summary>
    public string Token { get; set; } = string.Empty;
}
