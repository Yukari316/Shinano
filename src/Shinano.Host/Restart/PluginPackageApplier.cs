using Shinano.Host.Plugins.Staging;

namespace Shinano.Host.Restart;

/// <summary>
/// 按重启计划替换插件目录，并能还原。只在加载插件前调用：Windows 上已加载的程序集会锁定所在目录。
/// 备份目录位于插件目录内，原目录以同卷重命名移入备份，移动是原子的；第 i 项的备份路径固定为
/// <c>&lt;备份目录&gt;/&lt;i&gt;-&lt;目录名&gt;</c>，进程在任何一步中途退出后都能据此判断并还原。
/// </summary>
internal static class PluginPackageApplier
{
    /// <summary>备份目录名，位于插件目录下；其中不含顶层 <c>plugin.json</c>，不会被当作插件加载。</summary>
    public const string BACKUP_DIRECTORY_NAME = ".backup";

    /// <summary>插件目录对应的备份目录。</summary>
    public static string BackupRoot(string pluginRoot) => Path.Combine(pluginRoot, BACKUP_DIRECTORY_NAME);

    /// <summary>
    /// 把已有的目标目录移入备份，再把暂存的插件包移入目标目录。开始前清空备份目录，备份只保留最近一次。
    /// 失败时先还原再抛出。
    /// </summary>
    public static void Apply(RestartPlan plan, string pluginRoot, Action<RestartPlan> save)
    {
        string backupRoot = BackupRoot(pluginRoot);
        DeleteDirectory(backupRoot);
        Directory.CreateDirectory(backupRoot);
        plan.State = RestartPlanState.Applying;
        save(plan);
        try
        {
            for (int index = 0; index < plan.Operations.Count; index++)
            {
                PluginPackageOperation operation = plan.Operations[index];
                if (operation.Change.Kind != PluginChangeKind.Install)
                    Directory.Move(operation.TargetDirectory, BackupPath(backupRoot, index, operation));
                if (operation.StagedDirectory is { } staged)
                    MoveDirectory(staged, operation.TargetDirectory);
            }
        }
        catch
        {
            Restore(plan, pluginRoot);
            throw;
        }

        plan.State = RestartPlanState.Applied;
        save(plan);
    }

    /// <summary>按相反顺序撤销：删除新放入的目录，把备份移回原位，然后删除备份目录。</summary>
    public static void Restore(RestartPlan plan, string pluginRoot)
    {
        string backupRoot = BackupRoot(pluginRoot);
        for (int index = plan.Operations.Count - 1; index >= 0; index--)
        {
            PluginPackageOperation operation = plan.Operations[index];
            if (operation.Change.Kind == PluginChangeKind.Install)
            {
                // 建立计划时安装目标不存在，存在的只可能是新放入或复制了一半的目录。
                DeleteDirectory(operation.TargetDirectory);
                continue;
            }

            // 没有备份说明原目录尚未移走，仍在原位。
            string backup = BackupPath(backupRoot, index, operation);
            if (!Directory.Exists(backup)) continue;
            DeleteDirectory(operation.TargetDirectory);
            Directory.Move(backup, operation.TargetDirectory);
        }

        DeleteDirectory(backupRoot);
    }

    /// <summary>删除目录；不存在时不做任何事。</summary>
    public static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static string BackupPath(string backupRoot, int index, PluginPackageOperation operation) =>
        Path.Combine(backupRoot, $"{index}-{Path.GetFileName(operation.TargetDirectory)}");

    // 暂存区在部署数据目录，插件目录可以配置在其他卷上；跨卷时 Directory.Move 不可用，改为复制后删除。
    private static void MoveDirectory(string source, string destination)
    {
        if (string.Equals(
                Path.GetPathRoot(Path.GetFullPath(source)),
                Path.GetPathRoot(Path.GetFullPath(destination)),
                StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(source, destination);
            return;
        }

        CopyDirectory(source, destination);
        Directory.Delete(source, recursive: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
