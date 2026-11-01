namespace Shinano.Framework.Plugins;

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

    /// <summary>
    /// 声明机密类型，宿主在插件页显示机密表单，每个插件最多声明一次，并须与 <see cref="IPluginInitializationContext.Secrets{T}" /> 使用同一类型。
    /// 字符串属性可在面板只写：输入新值即替换，可清除，已保存的值不回显；其他类型的属性（如插件写入的登录状态）只显示是否已配置。
    /// 字段标题、说明与顺序取 <c>[Display(Name, Description, Order)]</c>，未标注时以属性名为标题。面板保存后
    /// <see cref="IPluginSecrets{T}.Value" /> 立即返回新值。
    /// </summary>
    /// <typeparam name="TSecrets">机密类型，无参构造结果即默认值。</typeparam>
    void UseSecrets<TSecrets>() where TSecrets : class, new();
}
