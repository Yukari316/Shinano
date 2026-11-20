using Shinano.Tool.Cli;

namespace Shinano.Tool.Deployment;

/// <summary>
/// 把目标部署的程序目录整体替换为新发布包中的版本。旧版移入 <c>.previous/</c>（替换上一次的备份），两个进程的日志与监控探测记录移回新版目录；
/// 任一步失败时还原为旧版。调用时目标部署的宿主与监控都已停止，部署数据目录不受影响。
/// </summary>
public static class ProgramSwap
{
    /// <summary>升级时保留下来的运行数据，相对部署根目录。</summary>
    public static readonly IReadOnlyList<string> PreservedPaths = ["host/Logs", "monitor/Logs", "monitor/data"];

    private const int    MOVE_ATTEMPTS    = 5;
    private const string DISCARDED_SUFFIX = ".failed-";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>执行替换。</summary>
    /// <param name="source">新发布包。</param>
    /// <param name="target">目标部署。</param>
    /// <param name="log">输出。</param>
    /// <exception cref="IOException">替换失败且已还原为旧版；消息说明失败原因。</exception>
    /// <exception cref="ToolException">替换失败，还原旧版也失败；消息说明旧版所在位置与手动还原方法。</exception>
    public static void Replace(DeploymentLayout source, DeploymentLayout target, ToolLog log)
    {
        if (Directory.Exists(target.PreviousDirectory)) Directory.Delete(target.PreviousDirectory, recursive: true);
        Directory.CreateDirectory(target.PreviousDirectory);

        List<string> moved = [];
        try
        {
            foreach (string name in DeploymentLayout.ProgramDirectoryNames)
            {
                string current = Path.Combine(target.Root, name);
                if (!Directory.Exists(current)) continue;
                Move(current, Path.Combine(target.PreviousDirectory, name));
                moved.Add(name);
            }

            foreach (string name in DeploymentLayout.ProgramDirectoryNames)
            {
                string from = Path.Combine(source.Root, name);
                if (Directory.Exists(from)) Copy(from, Path.Combine(target.Root, name));
            }

            foreach (string relative in PreservedPaths)
            {
                string kept = Path.Combine(target.PreviousDirectory, relative);
                if (!Directory.Exists(kept)) continue;
                string destination = Path.Combine(target.Root, relative);
                if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? target.Root);
                Move(kept, destination);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Error($"替换程序目录失败：{exception.Message}；正在还原旧版");
            try
            {
                Restore(target, moved, log);
            }
            catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
            {
                // 还原失败时不能报告"已还原"：旧版仍在 .previous/，由管理员手动移回。
                throw new ToolException(
                    $"替换程序目录失败（{exception.Message}），还原旧版也失败（{restoreError.Message}）。"
                    + $"旧版保存在 {target.PreviousDirectory}，请把其中的 {string.Join("、", moved)} 目录手动移回 {target.Root}");
            }

            throw new IOException($"替换程序目录失败，已还原旧版：{exception.Message}", exception);
        }

        log.Info($"已替换程序目录，旧版保存在 {target.PreviousDirectory}");
    }

    private static void Restore(DeploymentLayout target, IReadOnlyList<string> moved, ToolLog log)
    {
        // 已移入新版目录的运行数据先移回备份。
        foreach (string relative in PreservedPaths)
        {
            string current = Path.Combine(target.Root, relative);
            string kept = Path.Combine(target.PreviousDirectory, relative);
            if (Directory.Exists(current) && !Directory.Exists(kept))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(kept) ?? target.PreviousDirectory);
                Move(current, kept);
            }
        }

        // 新版目录先整体改名移开再把旧版移回：删除可能因文件被占用（如杀毒软件）只删掉一半，改名是一步完成的。
        List<string> discarded = [];
        foreach (string name in DeploymentLayout.ProgramDirectoryNames)
        {
            string current = Path.Combine(target.Root, name);
            if (!moved.Contains(name) || !Directory.Exists(current)) continue;
            string aside = Path.Combine(target.Root, $"{name}{DISCARDED_SUFFIX}{Guid.NewGuid():N}");
            Move(current, aside);
            discarded.Add(aside);
        }

        foreach (string name in moved)
            Move(Path.Combine(target.PreviousDirectory, name), Path.Combine(target.Root, name));

        foreach (string aside in discarded)
        {
            try
            {
                Directory.Delete(aside, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                log.Warning($"旧版已还原，但未能删除替换了一半的新版目录 {aside}：{exception.Message}；可以手动删除");
            }
        }
    }

    // 刚停止的进程可能还短暂占用文件（Windows），移动失败时重试几次。
    private static void Move(string from, string to)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (IOException) when (attempt < MOVE_ATTEMPTS)
            {
                Thread.Sleep(RetryDelay);
            }
        }
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(to, Path.GetRelativePath(from, file));
            File.Copy(file, destination, overwrite: true);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, File.GetUnixFileMode(file));
        }
    }
}
