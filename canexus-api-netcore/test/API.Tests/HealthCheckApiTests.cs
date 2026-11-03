
using System.Net;
using System.Text.Json;
using API;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace API.Tests;
/// <summary>
/// 健康检查端点集成测试：走真实 HTTP 管道验证 /health/live 与 /health/ready 的可用性，
/// 以及全局限流对探针路径的豁免（探针来自同一 IP 高频轮询，若被限流会导致负载均衡错误摘除正常实例）。
/// <para>就绪探测结果依赖外部依赖状态（数据库 / Redis / 调度器）：依赖健康返回 200、任一核心依赖不可达返回 503，
/// 两者均属合法响应；测试仅断言「响应结构合法且永不返回 429」，不与环境中的依赖状态耦合。</para>
/// <para>连接串可通过环境变量 SUPER_ADMIN_TEST_CONN 覆盖（沿用既有约定）。</para>
/// </summary>
[TestFixture]
public class HealthCheckApiTests
{
    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;

    /// <summary>测试套件初始化：启动测试宿主并启用极低的全局限流（验证探针豁免）。</summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var connectionString = ApiTestSupport.TestConnectionString;
        // API 项目根目录（appsettings.json 所在位置，含 Jwt 节、Serilog 等配置）
        var apiContentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "API"));
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddJsonFile(Path.Combine(apiContentRoot, "appsettings.json"), optional: false, reloadOnChange: false);
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = connectionString,
                        ["Jwt:Secret"] = ApiTestSupport.TestJwtSecret,
                        // 全局限流 3 次/分钟/IP：探针若未豁免，第 4 次请求即被限流
                        ["RateLimit:Enabled"] = "true",
                        ["RateLimit:GlobalPermitLimit"] = "3",
                        ["RateLimit:GlobalWindowSeconds"] = "60"
                    });
                });
            });

        _httpClient = _factory.CreateClient();
    }

    /// <summary>测试套件清理：释放宿主与资源。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
        _factory.Dispose();
    }

    /// <summary>验证 /health/live 恒返回 200（仅进程存活自检，不探测外部依赖）。</summary>
    [Test]
    public async Task HealthLive_AlwaysReturns200WithReportBody()
    {
        var response = await _httpClient.GetAsync("/health/live");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await AssertHealthReportBodyAsync(response);
    }

    /// <summary>验证 /health/ready 返回合法的健康报告（依赖健康 200 / 核心依赖不可达 503，均属合法）。</summary>
    [Test]
    public async Task HealthReady_ReturnsLegalReportDependingOnDependencies()
    {
        var response = await _httpClient.GetAsync("/health/ready");

        Assert.That(
            response.StatusCode,
            Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable),
            "依赖健康应为 200，核心依赖不可达应为 503，不应出现其他状态码");
        await AssertHealthReportBodyAsync(response);
    }

    /// <summary>验证探针路径豁免全局限流：远超限额的高频请求全部得到健康响应、无一次 429。</summary>
    [Test]
    public async Task HealthEndpoints_ExceedGlobalLimit_NeverRateLimited()
    {
        // 全局限流 3 次/分钟：未豁免时第 4 次起返回 429；探针路径应始终返回 200 / 503
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var response = await _httpClient.GetAsync("/health/live");
            Assert.That(
                response.StatusCode,
                Is.Not.EqualTo(HttpStatusCode.TooManyRequests),
                $"第 {attempt} 次探针请求不应被限流（/health 路径应在限流器中豁免）");
        }
    }

    /// <summary>断言响应体为合法的健康报告 JSON（含 status 字段与 entries 字典）。</summary>
    private static async Task AssertHealthReportBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.That(document.RootElement.TryGetProperty("status", out var status), Is.True, "健康报告应包含 status 字段");
        Assert.That(
            status.GetString(),
            Is.AnyOf("Healthy", "Degraded", "Unhealthy"),
            "status 应为 HealthStatus 的 JSON 序列化值");

        Assert.That(document.RootElement.TryGetProperty("entries", out var entries), Is.True, "健康报告应包含 entries 字典");
        Assert.That(entries.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }
}
