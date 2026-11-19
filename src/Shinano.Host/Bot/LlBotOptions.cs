namespace Shinano.Host.Bot;

/// <summary>LLBot（Milky）连接配置，对应配置节 <c>Shinano:LlBot</c>。AccessToken 只写在本地配置文件中。</summary>
public sealed class LlBotOptions
{
    /// <summary>配置节路径。</summary>
    public const string SECTION_NAME = "Shinano:LlBot";

    /// <summary>LLBot 主机。</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>LLBot 端口。</summary>
    public int Port { get; set; } = 3000;

    /// <summary>Milky 路径前缀，如 <c>milky</c> 对应 <c>/milky/api/...</c>；默认为空，即协议端直接在根路径提供接口。</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>访问令牌。</summary>
    public string AccessToken { get; set; } = string.Empty;
}
