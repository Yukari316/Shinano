using Shinano.Host.Notifications;
using Shinano.Host.Notifications.Models;

namespace Shinano.Tests.Notifications;

public sealed class NotificationCardRendererTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // 字体随宿主 Resource 目录输出；缺失时通知只能退回文字，因此验证资源可加载并能绘制中文与长文本。
    [Fact]
    public void Card_RendersAsPng_WithBundledResources()
    {
        using NotificationCardRenderer renderer = new(Path.Combine(AppContext.BaseDirectory, NotificationCardRenderer.RESOURCE_DIRECTORY));

        byte[] png = renderer.Render(
            new NotificationCard
            {
                Level = NotificationLevel.Warning,
                Title = "插件更新已回滚",
                Items =
                [
                    new NotificationItem { Label = "原因", Value = new string('长', 500) },
                    new NotificationItem { Label = "已还原", Value = "升级 在线检测 1.0.1 → 已还原为 1.0.0" }
                ]
            },
            DateTimeOffset.Now);

        Assert.Equal(PngSignature, png[..PngSignature.Length]);
    }
}
