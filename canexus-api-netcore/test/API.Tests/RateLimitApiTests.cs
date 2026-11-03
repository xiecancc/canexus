
using System.Net;
using API;
using Domain.Shared.Json;
using Domain.Shared.Results;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace API.Tests;
/// <summary>
/// 接口限流集成测试：通过 WebApplicationFactory 走真实 HTTP 管道，以极低限额验证认证端点
/// （auth 策略：验证码 / 登录 / 刷新令牌）被限流后返回统一 Result 结构（HTTP 429 + CommonTooManyRequests）。
/// <para>连接串可通过环境变量 SUPER_ADMIN_TEST_CONN 覆盖（沿用既有约定）。</para>
/// </summary>
[TestFixture]
public class RateLimitApiTests
{
    /// <summary>默认测试连接串（root 密码为本机约定值）。</summary>
    private const string DefaultConnectionString =
        "server=localhost;port=3306;database=super_admin_test;user id=root;password=***REMOVED***;CharSet=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=none";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;

    /// <summary>测试套件初始化：启动测试宿主并启用低限额限流配置。</summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var connectionString = Environment.GetEnvironmentVariable("SUPER_ADMIN_TEST_CONN") ?? DefaultConnectionString;
        // API 项目根目录（appsettings.json 所在位置，含 Jwt 节、Serilog 等配置）
        var apiContentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "API"));
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // 显式加载 API 项目的 appsettings.json，并以内存配置覆盖连接串与限流限额（内存配置后注册优先级高）
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddJsonFile(Path.Combine(apiContentRoot, "appsettings.json"), optional: false, reloadOnChange: false);
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = connectionString,
                        ["Jwt:Secret"] = ApiTestSupport.TestJwtSecret,
                        ["RateLimit:Enabled"] = "true",
                        ["RateLimit:AuthPermitLimit"] = "3",
                        ["RateLimit:AuthWindowSeconds"] = "60"
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

    /// <summary>验证认证端点超过限流限额后返回 429 及统一 Result 响应体。</summary>
    [Test]
    public async Task AuthCaptcha_ExceedsAuthLimit_Returns429WithResultBody()
    {
        // 限额 3 次：前 3 次正常返回，第 4 次触发限流（固定窗口按客户端 IP 分区）
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var allowed = await _httpClient.GetAsync("/api/auth/captcha");
            Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"第 {attempt} 次请求不应被限流");
        }

        var limited = await _httpClient.GetAsync("/api/auth/captcha");
        Assert.That(limited.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests), "第 4 次请求应被限流");

        // 响应体为统一 Result 结构，错误码为 CommonTooManyRequests
        var body = await limited.Content.ReadAsStringAsync();
        var result = JsonHelper.Deserialize<Result>(body);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Code, Is.EqualTo(ErrorCodes.CommonTooManyRequests));

        // 限流响应应携带 Retry-After 头（HTTP 客户端可能解析为秒数 Delta 或日期两种形式，均合法）
        Assert.That(limited.Headers.RetryAfter, Is.Not.Null, "限流响应应携带 Retry-After 头");
    }
}
