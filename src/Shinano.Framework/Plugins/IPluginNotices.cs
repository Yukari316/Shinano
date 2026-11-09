using Shinano.Framework.Plugins.Models;

namespace Shinano.Framework.Plugins;

/// <summary>插件经宿主通知全部 Super User。</summary>
public interface IPluginNotices
{
    /// <summary>
    /// 在后台以宿主通知卡片私聊全部 Super User，不等待结果；卡片无法绘制时改发文字，未连接 IM 或发送失败只记录日志，不重试。
    /// 去重（如同一故障只通知一次）由调用方负责。
    /// </summary>
    void NotifySuperUsers(SuperUserNotice notice);
}
