namespace Shinano.Framework.Plugins.Capabilities;

/// <summary>插件加载时声明的管理面板内容。</summary>
public interface IPanelRegistration
{
    /// <summary>
    /// 声明配置类型，宿主按其 JSON Schema 生成通用配置表单，每个插件最多声明一次。
    /// 字段标题、说明与顺序取 <c>[Display(Name, Description, Order)]</c>，未标注时以属性名为标题；
    /// DataAnnotations 校验特性在保存时于前端提示并由宿主再次校验。
    /// </summary>
    /// <typeparam name="TConfig">配置类型，无参构造结果即默认配置。</typeparam>
    void UseConfig<TConfig>() where TConfig : class, new();
}
