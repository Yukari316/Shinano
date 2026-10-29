namespace Shinano.Tool.Deployment;

/// <summary>
/// 工具的输出。<see cref="Say" /> 只写控制台；<see cref="Info" />、<see cref="Warning" />、<see cref="Error" />
/// 用于修改系统状态的操作（服务、用户、升级、插件安装、进程启停），同时追加到部署数据目录的 <c>tool.log</c>。
/// 调用方负责不传入密码、DSN 与密钥。
/// </summary>
public sealed class ToolLog
{
    private const string TIME_FORMAT = "yyyy-MM-dd HH:mm:ss";

    private readonly string  _path;
    private readonly string? _mirrorPath;
    private readonly Lock    _lock = new();

    /// <summary>创建输出。</summary>
    /// <param name="path"><c>tool.log</c> 的路径；所在目录不存在时创建。</param>
    /// <param name="mirrorPath">以管理员身份重新启动时，原进程读取并显示的输出文件；没有时为 null。</param>
    public ToolLog(string path, string? mirrorPath)
    {
        _path       = path;
        _mirrorPath = mirrorPath;
    }

    /// <summary>只写控制台的说明文字。</summary>
    public void Say(string message) => Write(message, record: false);

    /// <summary>记录一项操作或结果。</summary>
    public void Info(string message) => Write($"[{Now()} INF] {message}", record: true);

    /// <summary>记录一项需要注意的结果。</summary>
    public void Warning(string message) => Write($"[{Now()} WRN] {message}", record: true);

    /// <summary>记录一项失败，消息应包含可以采取的下一步。</summary>
    public void Error(string message) => Write($"[{Now()} ERR] {message}", record: true);

    private void Write(string line, bool record)
    {
        lock (_lock)
        {
            Console.WriteLine(line);
            if (_mirrorPath is not null) File.AppendAllText(_mirrorPath, line + Environment.NewLine);
            if (!record) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 记录失败不中断操作本身；控制台上已经有同样的内容。
                Console.WriteLine($"  （无法写入 {_path}：{exception.Message}）");
            }
        }
    }

    private static string Now() => DateTime.Now.ToString(TIME_FORMAT);
}
