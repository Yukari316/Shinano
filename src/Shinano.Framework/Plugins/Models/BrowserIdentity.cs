namespace Shinano.Framework.Plugins.Models;

/// <summary>浏览器对外呈现的身份，即每个请求都会带的 UA 与低熵 Client Hints 请求头的值。</summary>
public sealed class BrowserIdentity
{
    /// <summary><c>User-Agent</c>。</summary>
    public required string UserAgent { get; init; }

    /// <summary><c>sec-ch-ua</c>，如 <c>"Not-A.Brand";v="99", "Google Chrome";v="157", "Chromium";v="157"</c>。</summary>
    public required string SecChUa { get; init; }

    /// <summary><c>sec-ch-ua-mobile</c>，如 <c>?0</c>。</summary>
    public required string SecChUaMobile { get; init; }

    /// <summary><c>sec-ch-ua-platform</c>，如 <c>"Windows"</c>。</summary>
    public required string SecChUaPlatform { get; init; }

    /// <summary><c>Accept-Language</c>。</summary>
    public required string AcceptLanguage { get; init; }
}
