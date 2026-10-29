namespace Shinano.Tool.Run.Models;

/// <summary>守护者对一个宿主进程的就绪观察结果。</summary>
public enum ReadyOutcome
{
    /// <summary>启动流程已结束。</summary>
    Ready,

    /// <summary>进程退出前、停止前或时限内未就绪，进程保留。</summary>
    NotReady,

    /// <summary>未能及时就绪，已被守护者结束。</summary>
    Killed
}
