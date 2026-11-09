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

    /// <summary>
    /// 图源服务（YukariApiNext）的访问令牌，用于调用真实图源服务的测试。这类测试会消耗 SauceNAO 额度，
    /// 只在修改作品信息查询相关功能时手动设置运行，平时不设置。
    /// </summary>
    public static string? GatewayToken => Read("SHINANO_TEST_GATEWAY_TOKEN");

    /// <summary>图源服务地址；未设置时用插件的默认地址。</summary>
    public static string? GatewayUrl => Read("SHINANO_TEST_GATEWAY_URL");

    /// <summary>未设置图源服务令牌时跳过当前测试。</summary>
    public static void RequireGateway() =>
        Assert.SkipUnless(GatewayToken is not null, "未设置 SHINANO_TEST_GATEWAY_TOKEN 环境变量（调用真实图源服务，会消耗 SauceNAO 额度）");

    /// <summary>
    /// 浏览器测试使用的部署数据目录（下载的 Chrome for Testing 保存在其中，约 700 MB，重复运行时复用）。浏览器测试会访问
    /// Chrome for Testing 的版本与下载服务，只在修改浏览器服务时手动设置运行。
    /// </summary>
    public static string? BrowserDirectory => Read("SHINANO_TEST_BROWSER_DIR");

    /// <summary>未设置浏览器测试目录时跳过当前测试。</summary>
    public static void RequireBrowser() =>
        Assert.SkipUnless(BrowserDirectory is not null, "未设置 SHINANO_TEST_BROWSER_DIR 环境变量（会下载 Chrome for Testing）");

    private static string? Read(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
