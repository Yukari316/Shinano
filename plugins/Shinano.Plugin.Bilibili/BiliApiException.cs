namespace Shinano.Plugin.Bilibili;

/// <summary>B站接口返回了非 0 的业务码或 HTTP 错误。</summary>
public sealed class BiliApiException : Exception
{
    private const int NOT_LOGGED_IN = -101;
    private const int RISK_CONTROL  = -352;
    private const int INTERCEPTED   = -412;

    /// <summary>创建异常。</summary>
    /// <param name="code">业务码；HTTP 层错误时为负的 HTTP 状态码。</param>
    /// <param name="message">接口返回的消息。</param>
    public BiliApiException(int code, string message) : base($"B站接口返回 {code}：{message}")
    {
        Code = code;
    }

    /// <summary>业务码；HTTP 层错误时为负的 HTTP 状态码（如 HTTP 412 为 -412）。</summary>
    public int Code { get; }

    /// <summary>是否为风控拦截（<c>-352</c> 校验失败、<c>-412</c> 或 HTTP 412 请求被拦截）。</summary>
    public bool IsRiskControl => Code is RISK_CONTROL or INTERCEPTED;

    /// <summary>是否为未登录（<c>-101</c>）。</summary>
    public bool IsNotLoggedIn => Code == NOT_LOGGED_IN;
}
