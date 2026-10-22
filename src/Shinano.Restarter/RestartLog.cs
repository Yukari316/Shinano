namespace Shinano.Restarter;

/// <summary>
/// 辅助程序的日志：写入宿主日志目录下的 <c>&lt;日期&gt;-&lt;时间&gt;-restarter.log</c>，格式与宿主日志一致，
/// 面板的历史日志可以直接查看，宿主的日志目录清理同样覆盖它。
/// </summary>
internal sealed class RestartLog : IDisposable
{
    private const string FILE_NAME_FORMAT = "yyyy-MM-dd-HHmmss";
    private const string LINE_TIME_FORMAT = "yyyy-MM-dd HH:mm:ss.fff";
    private const string CATEGORY         = "Shinano.Restarter";

    private readonly StreamWriter _writer;

    public RestartLog(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{DateTime.Now.ToString(FILE_NAME_FORMAT)}-restarter.log");
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    public void Info(string message) => Write("INF", message);

    public void Warning(string message) => Write("WRN", message);

    public void Error(string message) => Write("ERR", message);

    public void Dispose() => _writer.Dispose();

    private void Write(string level, string message)
    {
        string line = $"[{DateTime.Now.ToString(LINE_TIME_FORMAT)} {level}] {CATEGORY}: {message}";
        _writer.WriteLine(line);
        Console.WriteLine(line);
    }
}
