using System.Text.Json;
using Shinano.Plugin.NB2Bridge.Models;

namespace Shinano.Plugin.NB2Bridge;

/// <summary>NB2 插件清单（<c>plugins.json</c>）的读写，也是插件启用状态的持久化位置。线程安全。</summary>
public sealed class Nb2Catalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly Nb2Layout            _layout;
    private readonly Lock                 _lock = new();
    private          List<Nb2PluginEntry>? _entries;

    /// <summary>创建清单访问。</summary>
    public Nb2Catalog(Nb2Layout layout)
    {
        _layout = layout;
    }

    /// <summary>全部已安装插件的快照；清单不存在时为空。</summary>
    public IReadOnlyList<Nb2PluginEntry> Entries()
    {
        lock (_lock) return [.. Load()];
    }

    /// <summary>查找插件。</summary>
    public Nb2PluginEntry? Find(string module)
    {
        lock (_lock) return Load().Find(entry => entry.Module == module);
    }

    /// <summary>设置启用状态并保存。</summary>
    /// <returns>插件不存在时为 false。</returns>
    public bool SetEnabled(string module, bool enabled)
    {
        lock (_lock)
        {
            if (Load().Find(entry => entry.Module == module) is not { } entry) return false;
            entry.Enabled = enabled;
            Save();
            return true;
        }
    }

    /// <summary>加入或替换（同模块名）插件并保存。</summary>
    public void Put(Nb2PluginEntry entry)
    {
        lock (_lock)
        {
            List<Nb2PluginEntry> entries = Load();
            entries.RemoveAll(item => item.Module == entry.Module);
            entries.Add(entry);
            Save();
        }
    }

    /// <summary>移除插件并保存。</summary>
    public void Remove(string module)
    {
        lock (_lock)
        {
            Load().RemoveAll(entry => entry.Module == module);
            Save();
        }
    }

    // 调用方持有 _lock。
    private List<Nb2PluginEntry> Load()
    {
        if (_entries is not null) return _entries;
        _entries = File.Exists(_layout.PluginsFile)
            ? JsonSerializer.Deserialize<List<Nb2PluginEntry>>(File.ReadAllText(_layout.PluginsFile), JsonOptions) ?? []
            : [];
        return _entries;
    }

    // 调用方持有 _lock。
    private void Save()
    {
        Directory.CreateDirectory(_layout.Root);
        string temporary = _layout.PluginsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_entries, JsonOptions));
        File.Move(temporary, _layout.PluginsFile, overwrite: true);
    }
}
