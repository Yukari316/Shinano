using Microsoft.Extensions.Logging;
using Shinano.Host.Plugins;
using Shinano.Infrastructure.Hosting;

namespace Shinano.Host.Bot;

/// <summary>
/// 让机器人账号进入可用状态：初始化账号数据，再激活持久化为启用的插件。
/// 失败后不定时重试，而是在每次连接与数据库、RustFS 恢复时再次执行；账号初始化成功后只重试未激活的插件。
/// </summary>
public sealed class AccountActivation
{
    private readonly HostInfrastructure         _infrastructure;
    private readonly PluginRuntime              _plugins;
    private readonly BotAccessor                _bot;
    private readonly ILogger<AccountActivation> _logger;
    private readonly SemaphoreSlim              _gate = new(1, 1);
    private          bool                       _accountReady;

    /// <summary>创建激活流程。</summary>
    public AccountActivation(
        HostInfrastructure         infrastructure,
        PluginRuntime              plugins,
        BotAccessor                bot,
        ILogger<AccountActivation> logger)
    {
        _infrastructure = infrastructure;
        _plugins        = plugins;
        _bot            = bot;
        _logger         = logger;
    }

    /// <summary>
    /// 执行一次激活；还没有连接过机器人账号时不做任何事。同一时刻只执行一次，失败只记录日志。
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        long selfId = _bot.SelfId;
        if (selfId == 0) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_accountReady)
            {
                await _infrastructure.InitializeAccountAsync(selfId, cancellationToken);
                _accountReady = true;
            }

            await _plugins.EnablePersistedAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "机器人账号 {SelfId} 初始化失败；重新连接或数据库、RustFS 恢复时重试", selfId);
        }
        finally
        {
            _gate.Release();
        }
    }
}
