using System.Net;

namespace Shinano.Host.Web;

/// <summary>
/// 只放行来自本机的请求：回环地址，或与面板监听地址相同的本机地址；其余返回 404，不暴露接口存在。
/// 用于供同机辅助程序与监控进程访问的免登录接口。
/// </summary>
public sealed class LocalRequestFilter : IEndpointFilter
{
    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ConnectionInfo connection = context.HttpContext.Connection;
        bool local = connection.RemoteIpAddress is { } remote
                     && (IPAddress.IsLoopback(remote) || remote.Equals(connection.LocalIpAddress));
        return local ? next(context) : ValueTask.FromResult<object?>(Results.NotFound());
    }
}
