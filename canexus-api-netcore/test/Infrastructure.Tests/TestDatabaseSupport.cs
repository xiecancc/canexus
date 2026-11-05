namespace Infrastructure.Tests;
/// <summary>
/// 基础设施层集成测试支撑（P1-R8）：与 API.Tests.ApiTestSupport 对齐——
/// 测试连接串强制经环境变量 SUPER_ADMIN_TEST_CONN 注入，代码中不内置任何密码回退值
///（历史内置的默认连接串属敏感信息入库，已移除）。
/// </summary>
internal static class TestDatabaseSupport
{
    /// <summary>测试连接串：强制经环境变量 SUPER_ADMIN_TEST_CONN 注入，缺失即快速失败。</summary>
    public static string TestConnectionString =>
        Environment.GetEnvironmentVariable("SUPER_ADMIN_TEST_CONN")
        ?? throw new InvalidOperationException(
            "缺少环境变量 SUPER_ADMIN_TEST_CONN（测试库连接串）。请先设置，例如："
            + "server=localhost;port=3306;database=super_admin_test;user id=root;password=<你的密码>;CharSet=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=none");
}
