using Autofac;
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Sora.Entities.Events;

namespace Shinano.Tests.SamplePluginA;

/// <summary>测试插件 A：容器注入的指令实例、事件订阅与初始化计数。</summary>
public sealed class SamplePlugin : IShinanoPlugin
{
    private static int InitializeCounter;

    /// <summary>InitializeAsync 被调用的次数。</summary>
    public static int InitializeCount => Volatile.Read(ref InitializeCounter);

    /// <inheritdoc />
    public void ConfigureServices(ContainerBuilder builder) => builder.RegisterType<Greeter>().SingleInstance();

    /// <inheritdoc />
    public void ConfigurePanel(IPanelRegistration panel)
    {
        panel.UseConfig<SampleConfig>();
        panel.UseSecrets<SampleSecrets>();
    }

    /// <inheritdoc />
    public ValueTask InitializeAsync(IPluginInitializationContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref InitializeCounter);
        context.Events.Subscribe<MessageReceivedEvent>(async e =>
        {
            if (e.Message.Body.GetText() != "hello") return;
            await e.ReplyAsync("world-a");
            e.IsContinueEventChain = !true;
        });
        return ValueTask.CompletedTask;
    }
}
