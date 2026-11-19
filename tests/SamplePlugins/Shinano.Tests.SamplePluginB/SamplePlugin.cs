using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Sora.Entities.Events;

namespace Shinano.Tests.SamplePluginB;

/// <summary>测试插件 B：容器注入的指令实例、事件订阅、初始化计数，以及只有专有面板（页面与 API）、没有配置表单。</summary>
public sealed class SamplePlugin : IShinanoPlugin
{
    /// <summary>
    /// 部署数据目录中存在此文件时，下一次初始化在登记页面、API 与事件订阅之后删除它并失败，用于验证重试不会重复登记。
    /// 插件在独立的加载上下文中运行，测试无法直接改它的静态状态，所以用文件传递。
    /// </summary>
    public const string FAIL_ONCE_FILE = "sample-b-fail-once";

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

        string failOnce = Path.Combine(context.DataDirectory, FAIL_ONCE_FILE);
        if (File.Exists(failOnce))
        {
            File.Delete(failOnce);
            throw new InvalidOperationException("模拟的初始化失败");
        }

        return ValueTask.CompletedTask;
    }
}
