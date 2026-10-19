namespace Shinano.Tests.Infrastructure;

/// <summary>
/// 集成测试的外部服务配置，来自环境变量；未设置时相关测试跳过。
/// 只允许指向测试数据库与测试 RustFS，测试只创建并清理以本次运行 ID 命名的 schema 与 bucket。
/// </summary>
public static class TestEnvironment
{
    /// <summary>测试数据库 DSN。</summary>
    public static string? Dsn => Read("SHINANO_TEST_DSN");

    /// <summary>测试 RustFS 地址。</summary>
    public static string? S3Endpoint => Read("SHINANO_TEST_S3_ENDPOINT");

    /// <summary>测试 RustFS 访问密钥 ID。</summary>
    public static string? S3AccessKey => Read("SHINANO_TEST_S3_ACCESS_KEY");

    /// <summary>测试 RustFS 访问密钥。</summary>
    public static string? S3SecretKey => Read("SHINANO_TEST_S3_SECRET_KEY");

    /// <summary>数据库与 RustFS 是否都已配置。</summary>
    public static bool IsConfigured =>
        Dsn is not null && S3Endpoint is not null && S3AccessKey is not null && S3SecretKey is not null;

    /// <summary>未配置时跳过当前测试。</summary>
    public static void RequireConfigured() =>
        Assert.SkipUnless(IsConfigured, "未设置 SHINANO_TEST_DSN 与 SHINANO_TEST_S3_* 环境变量");

    private static string? Read(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
