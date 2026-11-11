using Shinano.Tool.HsoImport;
using Shinano.Tool.Run;
using Shinano.Tool.Services;
using Shinano.Tool.Setup;
using Shinano.Tool.Upgrade;

namespace Shinano.Tool.Cli;

/// <summary>按子命令分派。</summary>
public static class ToolApplication
{
    private const string USAGE = """
                                 用法：Shinano.Tool <命令> [选项]
                                   init                 初始化或重新配置本部署（交互问答；--accept-defaults 全部取默认值或现有值）
                                   run                  直接运行：在前台守护宿主与监控，按 Ctrl+C 停止
                                   upgrade --target <目录>
                                                        用本发布包升级指定的部署
                                   service <install|uninstall|start|stop|status>
                                                        管理本部署的系统服务
                                   hso-import --dir <目录> [--dsn <DSN>] [--rustfs-endpoint <地址>] [--access-key <AccessKey>]
                                              [--secret-key <SecretKey>] [--bucket-prefix <前缀>]
                                                        把目录中的图片导入 Hso 图库（需先在面板启用过一次 Hso 插件）；
                                                        数据库与 RustFS 默认取本部署的本地配置
                                   nb2-setup [--data-dir <目录>]
                                                        用 uv 在部署数据目录的 nb2/ 下创建或补全 NB2 运行环境
                                 通常通过 scripts 下的 init、start、upgrade、service、hso-import、nb2-setup 脚本调用。
                                 """;

    /// <summary>运行工具。</summary>
    /// <returns>进程退出码：成功为 0，失败为 1，需要管理员手动执行命令时为 2。</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            CommandArguments arguments = CommandArguments.Parse(args);
            string command = arguments.Positionals.FirstOrDefault() ?? string.Empty;
            (IReadOnlyCollection<string> options, Func<ToolContext, Task<int>> handler)? entry = command switch
            {
                "init"       => (InitCommand.Options, InitCommand.RunAsync),
                "run"        => (Supervisor.Options, Supervisor.RunAsync),
                "upgrade"    => (UpgradeCommand.Options, UpgradeCommand.RunAsync),
                "service"    => (ServiceCommand.Options, ServiceCommand.RunAsync),
                "hso-import" => (HsoImportCommand.Options, HsoImportCommand.RunAsync),
                "nb2-setup"  => (Nb2SetupCommand.Options, Nb2SetupCommand.RunAsync),
                _            => null
            };
            if (entry is not { } known)
            {
                Console.Error.WriteLine(USAGE);
                return 1;
            }

            arguments.RequireKnown(known.options);
            return await known.handler(ToolContext.Create(arguments));
        }
        catch (ToolException exception)
        {
            Console.Error.WriteLine($"错误：{exception.Message}");
            return 1;
        }
    }
}
