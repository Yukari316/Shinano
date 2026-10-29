using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Shinano.Tool.Run;

/// <summary>向其他进程发送 POSIX 信号；.NET 的进程 API 只能发送 SIGKILL。</summary>
[UnsupportedOSPlatform("windows")]
public static class UnixSignal
{
    private const int SIGTERM = 15;

    /// <summary>请求进程正常退出；进程已退出时忽略。</summary>
    public static void Terminate(int processId) => _ = Kill(processId, SIGTERM);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int processId, int signal);
}
