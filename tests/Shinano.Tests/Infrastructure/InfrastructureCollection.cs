namespace Shinano.Tests.Infrastructure;

/// <summary>共用同一套真实基础设施的集成测试集合。</summary>
[CollectionDefinition(NAME)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    /// <summary>集合名称。</summary>
    public const string NAME = "Infrastructure";
}
