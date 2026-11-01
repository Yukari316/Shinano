namespace Shinano.Host.Panel.Models;

/// <summary>保存宿主配置前对一组连接配置的实际检查结果。</summary>
public sealed class ConnectionCheck
{
    /// <summary>检查是否通过。</summary>
    public required bool Succeeded { get; init; }

    /// <summary>面向管理员的说明：失败时为原因，成功时为补充信息（如连上的账号），没有时为空。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>通过且没有补充信息。</summary>
    public static ConnectionCheck Passed { get; } = new() { Succeeded = true };

    /// <summary>通过并附带补充信息。</summary>
    public static ConnectionCheck PassedWith(string message) =>
        new()
        {
            Succeeded = true,
            Message   = message
        };

    /// <summary>未通过。</summary>
    public static ConnectionCheck Failed(string message) =>
        new()
        {
            Succeeded = false,
            Message   = message
        };
}
