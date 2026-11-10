using Microsoft.Extensions.Logging;
using Shinano.Host.Plugins.Models;
using Shinano.Host.Restart;
using System.IO.Compression;

namespace Shinano.Host.Plugins;

/// <summary>
/// 插件包变更的暂存区，位于部署数据目录的 <c>plugin-staging/</c>：上传的插件包校验后解压到 <c>packages/&lt;id&gt;/</c>，
/// 待处理变更记录在 <c>pending.json</c>。暂存的变更只由面板的重启操作应用，普通启动不应用。
/// 同一插件再次上传或删除时覆盖原有暂存，每项可撤销。
/// </summary>
public sealed class PluginPackageStaging
{
    /// <summary>上传插件包的大小上限（字节）。</summary>
    public const long MAX_PACKAGE_BYTES = 100L * 1024 * 1024;

    /// <summary>解压后的大小上限（字节），防止压缩炸弹。</summary>
    public const long MAX_EXTRACTED_BYTES = 512L * 1024 * 1024;

    private const string DIRECTORY_NAME  = "plugin-staging";
    private const string PACKAGES_NAME   = "packages";
    private const string PENDING_FILE    = "pending.json";
    private const string INCOMING_PREFIX = ".incoming-";
    private const int    BYTES_PER_MIB   = 1024 * 1024;
    private const int    COPY_BUFFER     = 81920;

    private readonly string                        _pluginRoot;
    private readonly TimeProvider                  _time;
    private readonly ILogger<PluginPackageStaging> _logger;
    private readonly Lock                          _lock = new();
    private          bool                          _frozen;

    /// <summary>创建暂存区，并清理上次未完成的上传。</summary>
    public PluginPackageStaging(PluginRuntimeSettings settings, TimeProvider time, ILogger<PluginPackageStaging> logger)
    {
        _pluginRoot = settings.PluginRoot;
        _time       = time;
        _logger     = logger;
        Root        = Path.Combine(settings.DataDirectory, DIRECTORY_NAME);
        Directory.CreateDirectory(Root);
        foreach (string incoming in Directory.EnumerateFileSystemEntries(Root, INCOMING_PREFIX + "*"))
            DeleteEntry(incoming);
    }

    /// <summary>暂存区目录。</summary>
    public string Root { get; }

    private string PendingPath => Path.Combine(Root, PENDING_FILE);

    /// <summary>插件 ID 对应的暂存插件包目录。</summary>
    public string PackageDirectory(string pluginId) => Path.Combine(Root, PACKAGES_NAME, pluginId);

    /// <summary>全部待处理变更，按暂存时间排序。</summary>
    public IReadOnlyList<PendingPluginChange> Pending()
    {
        lock (_lock)
            return Read();
    }

    /// <summary>
    /// 校验并暂存上传的插件包。插件包根目录须有有效的 <c>plugin.json</c> 与入口程序集，条目不能越出插件目录，
    /// bucket 用途不能与其他插件冲突。已安装同 ID 插件时记为升级，不比较版本（允许降级）。
    /// </summary>
    /// <exception cref="InvalidDataException">插件包无效；消息可直接展示给管理员。</exception>
    /// <exception cref="InvalidOperationException">正在重启，不再接受变更。</exception>
    public async Task<PendingPluginChange> StageAsync(Stream upload, CancellationToken cancellationToken)
    {
        string incoming = Path.Combine(Root, INCOMING_PREFIX + Guid.NewGuid().ToString("N"));
        string archive  = incoming + ".zip";
        try
        {
            long size = await CopyLimitedAsync(upload, archive, cancellationToken);
            Extract(archive, incoming);
            PluginManifest manifest = ReadManifest(incoming);
            lock (_lock)
            {
                ThrowIfFrozen();
                List<PendingPluginChange> pending = Read();
                CheckBucketPurposes(manifest, pending);

                PluginManifest? installed = FindInstalled(manifest.Id)?.Manifest;
                PendingPluginChange change = new()
                {
                    Change = new PluginChange
                    {
                        Kind        = installed is null ? PluginChangeKind.Install : PluginChangeKind.Upgrade,
                        PluginId    = manifest.Id,
                        Name        = DisplayName(manifest),
                        FromVersion = installed?.Version,
                        ToVersion   = manifest.Version
                    },
                    StagedAt  = _time.GetUtcNow(),
                    SizeBytes = size
                };

                string target = PackageDirectory(manifest.Id);
                DeleteEntry(target);
                Directory.CreateDirectory(Path.Combine(Root, PACKAGES_NAME));
                Directory.Move(incoming, target);
                Write([.. pending.Where(item => item.Change.PluginId != manifest.Id), change]);
                _logger.LogInformation(
                    "已暂存插件包 {PluginId}：{Kind} {FromVersion} → {ToVersion}，重启后生效",
                    manifest.Id,
                    change.Change.Kind,
                    change.Change.FromVersion,
                    change.Change.ToVersion);
                return change;
            }
        }
        finally
        {
            DeleteEntry(archive);
            DeleteEntry(incoming);
        }
    }

    /// <summary>
    /// 暂存删除已安装的插件包，覆盖该插件原有的暂存。<paramref name="purgeData" /> 为 false 时插件数据保留，
    /// 为 true 时在重启确认成功后清除。插件未安装但有暂存的安装时，等同撤销该安装。
    /// </summary>
    /// <returns>暂存的删除；插件未安装时为 null。</returns>
    /// <exception cref="InvalidOperationException">正在重启，不再接受变更。</exception>
    public PendingPluginChange? StageRemoval(string pluginId, bool purgeData)
    {
        lock (_lock)
        {
            ThrowIfFrozen();
            List<PendingPluginChange> pending = Read();
            if (FindInstalled(pluginId)?.Manifest is not { } manifest)
            {
                if (pending.Any(item => item.Change.PluginId == pluginId)) Undo(pluginId, pending);
                return null;
            }

            DeleteEntry(PackageDirectory(pluginId));
            PendingPluginChange change = new()
            {
                Change = new PluginChange
                {
                    Kind        = PluginChangeKind.Remove,
                    PluginId    = pluginId,
                    Name        = DisplayName(manifest),
                    FromVersion = manifest.Version,
                    PurgeData   = purgeData
                },
                StagedAt  = _time.GetUtcNow(),
                SizeBytes = 0
            };
            Write([.. pending.Where(item => item.Change.PluginId != pluginId), change]);
            _logger.LogInformation("已暂存删除插件包 {PluginId}（清除数据：{PurgeData}），重启后生效", pluginId, purgeData);
            return change;
        }
    }

    /// <summary>撤销插件的暂存变更。</summary>
    /// <returns>是否存在并已撤销。</returns>
    /// <exception cref="InvalidOperationException">正在重启，不再接受变更。</exception>
    public bool Undo(string pluginId)
    {
        lock (_lock)
        {
            ThrowIfFrozen();
            List<PendingPluginChange> pending = Read();
            if (pending.All(item => item.Change.PluginId != pluginId)) return false;
            Undo(pluginId, pending);
            return true;
        }
    }

    /// <summary>重启开始后不再接受新的变更，使应用的变更与重启确认时列出的一致。</summary>
    /// <returns>冻结时的待处理变更。</returns>
    public IReadOnlyList<PendingPluginChange> Freeze()
    {
        lock (_lock)
        {
            _frozen = true;
            return Read();
        }
    }

    /// <summary>取消冻结，用于重启未能发起时。</summary>
    public void Unfreeze()
    {
        lock (_lock)
            _frozen = false;
    }

    /// <summary>从待处理列表移除已应用的变更。只在启动阶段应用插件包后调用。</summary>
    public void Consume(IReadOnlyCollection<string> pluginIds)
    {
        lock (_lock)
            Write([.. Read().Where(item => !pluginIds.Contains(item.Change.PluginId))]);
    }

    /// <summary>已安装插件包中 ID 匹配的一个；没有时为 null。</summary>
    public PluginPackageDirectory? FindInstalled(string pluginId) =>
        PluginPackageScanner.Scan(_pluginRoot).FirstOrDefault(package => package.Manifest?.Id == pluginId);

    private static string DisplayName(PluginManifest manifest) =>
        string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name;

    private void Undo(string pluginId, List<PendingPluginChange> pending)
    {
        DeleteEntry(PackageDirectory(pluginId));
        Write([.. pending.Where(item => item.Change.PluginId != pluginId)]);
        _logger.LogInformation("已撤销插件 {PluginId} 的暂存变更", pluginId);
    }

    private void ThrowIfFrozen()
    {
        if (_frozen) throw new InvalidOperationException("正在重启，不再接受插件包变更");
    }

    // 待删除的已安装插件释放其用途；暂存中的其他插件包同样占用用途。
    private void CheckBucketPurposes(PluginManifest manifest, List<PendingPluginChange> pending)
    {
        HashSet<string> removing =
        [
            .. pending.Where(item => item.Change.Kind == PluginChangeKind.Remove).Select(item => item.Change.PluginId)
        ];
        IEnumerable<PluginManifest> installed =
            PluginPackageScanner.Scan(_pluginRoot)
                                .Select(package => package.Manifest)
                                .OfType<PluginManifest>()
                                .Where(other => other.Id != manifest.Id && !removing.Contains(other.Id));
        IEnumerable<PluginManifest> staged =
            pending.Where(item => item.Change.Kind != PluginChangeKind.Remove && item.Change.PluginId != manifest.Id)
                   .Select(item => ReadManifest(PackageDirectory(item.Change.PluginId)));
        foreach (PluginManifest other in installed.Concat(staged))
            if (manifest.BucketPurposes.Intersect(other.BucketPurposes, StringComparer.Ordinal).FirstOrDefault() is { } purpose)
                throw new InvalidDataException($"bucket 用途 {purpose} 已被插件 {other.Id} 使用");
    }

    private static async Task<long> CopyLimitedAsync(Stream upload, string path, CancellationToken cancellationToken)
    {
        await using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write);
        byte[] buffer = new byte[COPY_BUFFER];
        long total = 0;
        int read;
        while ((read = await upload.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MAX_PACKAGE_BYTES)
                throw new InvalidDataException($"插件包超过 {MAX_PACKAGE_BYTES / BYTES_PER_MIB} MiB");
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return total;
    }

    private static void Extract(string archivePath, string destination)
    {
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(archivePath);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("插件包不是有效的 zip 文件");
        }

        using (archive)
        {
            long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string path = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!path.StartsWith(root, StringComparison.Ordinal))
                    throw new InvalidDataException($"插件包条目 {entry.FullName} 越出插件目录");
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(path);
                    continue;
                }

                total += entry.Length;
                if (total > MAX_EXTRACTED_BYTES)
                    throw new InvalidDataException($"插件包解压后超过 {MAX_EXTRACTED_BYTES / BYTES_PER_MIB} MiB");
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
                entry.ExtractToFile(path);
            }
        }
    }

    private static PluginManifest ReadManifest(string directory)
    {
        string manifestPath = Path.Combine(directory, PluginManifest.FILE_NAME);
        if (!File.Exists(manifestPath)) throw new InvalidDataException($"插件包根目录缺少 {PluginManifest.FILE_NAME}");

        PluginManifest manifest = PluginManifest.Read(manifestPath);
        if (!File.Exists(Path.Combine(directory, manifest.EntryAssembly)))
            throw new InvalidDataException($"插件包缺少入口程序集 {manifest.EntryAssembly}");
        return manifest;
    }

    private List<PendingPluginChange> Read() =>
        [.. (StateFile.Read<List<PendingPluginChange>>(PendingPath) ?? []).OrderBy(item => item.StagedAt)];

    private void Write(IReadOnlyList<PendingPluginChange> pending) => StateFile.Write(PendingPath, pending);

    private static void DeleteEntry(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
    }
}
