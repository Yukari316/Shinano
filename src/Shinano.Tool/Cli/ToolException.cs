namespace Shinano.Tool.Cli;

/// <summary>可以直接展示给管理员的失败：工具输出消息并以退出码 1 结束，不输出堆栈。</summary>
public sealed class ToolException : Exception
{
    /// <summary>创建失败。</summary>
    /// <param name="message">展示给管理员的说明，应包含可以采取的下一步。</param>
    public ToolException(string message) : base(message)
    {
    }
}
