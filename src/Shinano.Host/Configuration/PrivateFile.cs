using System.Text;

namespace Shinano.Host.Configuration;

/// <summary>含机密的部署文件：只允许属主读写。</summary>
internal static class PrivateFile
{
    /// <summary>
    /// 先写临时文件再替换 <paramref name="path" />（UTF-8 无 BOM）。非 Windows 上文件以 0600 创建；
    /// Windows 依赖部署目录的 ACL。
    /// </summary>
    public static void Write(string path, string contents)
    {
        string temporary = path + ".tmp";
        // 残留的临时文件可能是旧权限，删除后重新创建才会套用新权限。
        File.Delete(temporary);
        FileStreamOptions options = new()
        {
            Mode   = FileMode.CreateNew,
            Access = FileAccess.Write
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (StreamWriter writer = new(temporary, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), options))
            writer.Write(contents);
        File.Move(temporary, path, overwrite: true);
    }
}
