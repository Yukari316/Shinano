using Shinano.Framework.Plugins;
using Shinano.Host.Configuration;
using Shinano.Host.Plugins.Config;
using System.Text.Json;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>
/// 插件机密文件 <c>&lt;部署数据目录&gt;/secrets/&lt;plugin-id&gt;.json</c> 的读写。文件不存在时为默认值；
/// 写入时先写临时文件再替换，避免中断留下半个文件。
/// </summary>
internal sealed class PluginSecrets<T> : IPluginSecrets<T>, IPluginSecretsHolder where T : class, new()
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string        _path;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private volatile T             _value;

    /// <summary>读取机密文件。</summary>
    /// <exception cref="JsonException">文件内容与机密类型不兼容。</exception>
    public PluginSecrets(string path)
    {
        _path  = path;
        _value = File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? new T() : new T();
    }

    /// <inheritdoc />
    public T Value => _value;

    /// <inheritdoc />
    public object Current => _value;

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(Func<object, object?> apply, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            // SAFE: 面板只按声明的机密类型构造新值。
            if (apply(_value) is not { } next) return false;
            Write((T)next);
            return true;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(T value, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            Write(value);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    // 调用方持有写入锁。
    private void Write(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        PrivateFile.Write(_path, JsonSerializer.Serialize(value, JsonOptions));
        _value = value;
    }
}
