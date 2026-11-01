namespace Shinano.Host.Panel.Models;

/// <summary>保存宿主配置的结果。</summary>
public sealed class HostConfigSaveResult
{
    /// <summary>校验或连接检查的错误；不为空时没有写入任何修改。</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>检查通过时的补充信息，如 LLBot 连上的账号。</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
