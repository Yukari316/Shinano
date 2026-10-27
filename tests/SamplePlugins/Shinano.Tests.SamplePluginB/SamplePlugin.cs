using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Shinano.Framework.Messaging;
using Shinano.Framework.Plugins;
using Shinano.Framework.Plugins.Capabilities;
using Sora.Entities.Events;

namespace Shinano.Tests.SamplePluginB;

/// <summary>测试插件 B：容器注入的指令实例、事件订阅、初始化计数，以及只有专有面板（页面与 API）、没有配置表单。</summary>
public sealed class SamplePlugin : IShinanoPlugin
{
    private static int _initializeCount;

    /// <summary>InitializeAsync 被调用的次数。</summary>
    public static int InitializeCount => Volatile.Read(ref _initializeCount);

    /// <inheritdoc />
    public void ConfigureServices(ContainerBuilder builder) => builder.RegisterType<Greeter>().SingleInstance();

    /// <inheritdoc />
    public void ConfigurePanel(IPanelRegistration panel)
    {
    }

    /// <inheritdoc />
    public ValueTask InitializeAsync(IPluginInitializationContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _initializeCount);
        context.Panel.UsePage("样例页面", "index.js");
        context.Panel.MapApi(api => api.MapGet("/ping", () => "pong-b"));
        context.Events.Subscribe<MessageReceivedEvent>(async e =>
        {
            if (e.Message.Body.GetText() != "hello") return;
            await e.ReplyAsync("world-b");
            e.IsContinueEventChain = !false;
        });
        return ValueTask.CompletedTask;
    }
}
