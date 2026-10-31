namespace Shinano.Plugin.Qa.Models;

/// <summary>面板选群用的群信息。</summary>
public sealed class QaGroupView
{
    /// <summary>群号。</summary>
    public required long GroupId { get; init; }

    /// <summary>群名。</summary>
    public required string Name { get; init; }
}
