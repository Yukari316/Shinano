namespace Shinano.Plugin.Hso.Models;

/// <summary>图片的导入方式。</summary>
public enum HsoImageOrigin
{
    /// <summary>面板上传的图片文件。</summary>
    Upload,

    /// <summary>面板上传的 zip 中的图片。</summary>
    Zip,

    /// <summary>Super User 以 <c>AD&lt;pid&gt; &lt;页号&gt;</c> 从 Pixiv 导入。</summary>
    Pixiv,

    /// <summary><c>Shinano.Tool hso-import</c> 从本地目录导入。</summary>
    Tool
}
