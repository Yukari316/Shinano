using System.Text;
using System.Text.RegularExpressions;

namespace Shinano.Plugin.NB2Bridge.Runtime;

/// <summary>
/// NB2 进程输出的落盘与最近若干行的内存缓存。文件为 <c>logs/nb2.log</c>，超过大小上限时轮转为 <c>nb2.1.log</c>、<c>nb2.2.log</c>……
/// 只保留配置的个数。线程安全；跨进程重启复用同一实例，内存中的最近行不因重启清空。
/// </summary>
public sealed partial class Nb2LogSink : IDisposable
{
    /// <summary>内存中保留的最近行数。</summary>
    public const int RECENT_CAPACITY = 500;

    private const string FILE_NAME      = "nb2";
    private const string EXTENSION      = ".log";
    private const string REDACTED_TOKEN = "access_token=***";

    private readonly string        _directory;
    private readonly Lock          _lock   = new();
    private readonly Queue<string> _recent = new();
    private          StreamWriter? _writer;
    private          long          _maxBytes;
    private          int           _maxFiles;

    /// <summary>创建日志输出。</summary>
    /// <param name="directory">日志目录。</param>
    public Nb2LogSink(string directory)
    {
        _directory = directory;
    }

    private string CurrentPath => Path.Combine(_directory, FILE_NAME + EXTENSION);

    /// <summary>按新的轮转参数打开日志文件（每次启动 NB2 时调用）。</summary>
    public void Open(long maxBytes, int maxFiles)
    {
        lock (_lock)
        {
            _maxBytes = maxBytes;
            _maxFiles = maxFiles;
            Directory.CreateDirectory(_directory);
            _writer?.Dispose();
            _writer = new StreamWriter(CurrentPath, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        }
    }

    /// <summary>写入一行；去掉 ANSI 颜色码（NoneBot 的 loguru 输出强制着色），并隐去适配器连接出错时打印在地址中的 token。</summary>
    public void Write(string raw)
    {
        string line = TokenQuery().Replace(AnsiEscape().Replace(raw, string.Empty), REDACTED_TOKEN);
        lock (_lock)
        {
            _recent.Enqueue(line);
            while (_recent.Count > RECENT_CAPACITY) _recent.Dequeue();
            if (_writer is null) return;

            _writer.WriteLine(line);
            if (_writer.BaseStream.Length >= _maxBytes) Rotate();
        }
    }

    /// <summary>最近的若干行，按时间先后。</summary>
    public IReadOnlyList<string> Recent(int count)
    {
        lock (_lock) return [.. _recent.TakeLast(count)];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    // 调用方持有 _lock。
    private void Rotate()
    {
        // SAFE: Write 已确认 _writer 不为 null。
        _writer!.Dispose();
        // 保留个数含当前文件：编号文件最多到 _maxFiles - 1，超出的最旧一份直接被覆盖。
        for (int index = _maxFiles - 2; index >= 1; index--)
        {
            if (File.Exists(Numbered(index))) File.Move(Numbered(index), Numbered(index + 1), overwrite: true);
        }

        if (_maxFiles > 1) File.Move(CurrentPath, Numbered(1), overwrite: true);
        else File.Delete(CurrentPath);
        _writer = new StreamWriter(CurrentPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    private string Numbered(int index) => Path.Combine(_directory, $"{FILE_NAME}.{index}{EXTENSION}");

    [GeneratedRegex("\u001b\\[[0-9;]*m")]
    private static partial Regex AnsiEscape();

    [GeneratedRegex(@"access_token=[^&\s""']+")]
    private static partial Regex TokenQuery();
}
