namespace Shinano.Framework.Plugins.Models;

/// <summary>直接调用 Milky API 的原始结果。</summary>
public sealed class MilkyCallResult
{
    /// <summary>HTTP 状态码；200 时正文为 Milky 响应 JSON（<c>status</c>、<c>retcode</c>、<c>data</c>、<c>message</c>）。</summary>
    public required int StatusCode { get; init; }

    /// <summary>响应正文原文。</summary>
    public required string Body { get; init; }
}
