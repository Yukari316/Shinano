namespace Shinano.Framework.Rendering;

/// <summary>卡片的等级，只体现在菱形状态标与类型标签的颜色上。</summary>
public enum CardLevel
{
    /// <summary>正常（信号黄；用于状态项时为绿色）。</summary>
    Normal,

    /// <summary>警告（橙色），如相似度低于阈值、插件更新回滚。</summary>
    Warning,

    /// <summary>错误（红色），如数据库不可用。</summary>
    Error
}
