using System.Text.Json.Serialization;

namespace Shinano.Plugin.NB2Bridge.Models;

/// <summary>传给 NB2 进程的一个环境变量。</summary>
public sealed class Nb2EnvironmentVariable
{
    /// <summary>变量名。</summary>
    public required string Name { get; init; }

    /// <summary>实际值，只用于启动进程；含 token，不序列化给页面。</summary>
    [JsonIgnore]
    public string Value { get; init; } = string.Empty;

    /// <summary>页面显示的值：token 显示为已设置，PATH 只显示前置的目录。</summary>
    public required string Display { get; init; }
}
