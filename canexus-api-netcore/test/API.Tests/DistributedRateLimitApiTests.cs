using System.Net;
using API;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace API.Tests;

/// <summary>
/// 分布式限流（P1-A5）集成测试：
/// 验证开启 Redis 分布式限流后，多副本（两个独立宿主进程）共享同一配额——
/// 副本 A 消耗的配额在副本 B 可见，超限请求返回统一 429。
/// <para>使用本机 Redis（127.0.0.1:6379）独立数据库（defaultDatabase=3），测试前 FLUSHDB 隔离。</para>
/// </summary>
[TestFixture]
public class DistributedRateLimitApiTests
{
    private const string TestRedis = "localhost:6379,defaultDatabase=3,abortConnect=false";
    private const string TestRedisPrefix = "rl-test:";
    private const int PermitLimit = 6;

    /// <summary>
    /// 固定窗口长度（秒）：由 60 调整为 300，配合 <see cref="ResetQuotaAsync"/> 的窗口对齐，
    /// 消除「用例执行中恰好跨过窗口边界导致计数归零、本应 429 的请求返回 200」的偶发失败。
    /// 窗口起点取自 Redis 服务端 TIME（与限流器同口径），故此处也按 Redis 时钟对齐。
    /// </summary>
    private const int WindowSeconds = 300;

    /// <summary>开始用例前要求当前固定窗口至少剩余的秒数（覆盖预热 + 7 次请求的耗时）。</summary>
    private const int MinWindowRemainingSeconds = 20;

    private static WebApplicationFactory<Program> _hostA = null!;
    private static WebApplicationFactory<Program> _hostB = null!;
    private static HttpClient _clientA = null!;
    private static HttpClient _clientB = null!;

    /// <summary>套件初始化：清空测试 Redis 库，启动两个独立宿主（同配置、同 Redis）。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        // 隔离：按前缀清理测试专用 Redis 数据库中的历史限流键（SCAN+DEL，无需 admin 模式）
        await ResetQuotaAsync();

        var connectionString = ApiTestSupport.TestConnectionString;
        var apiContentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "API"));

        WebApplicationFactory<Program> CreateHost(string suffix)
        {
            var factory = new ApiTestFactory()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureAppConfiguration((_, configuration) =>
                    {
                        configuration.AddJsonFile(Path.Combine(apiContentRoot, "appsettings.json"), optional: false, reloadOnChange: false);
                        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Database:ConnectionString"] = connectionString,
                            ["Jwt:Secret"] = ApiTestSupport.TestJwtSecret,
                            ["RateLimit:Enabled"] = "true",
                            ["RateLimit:UseRedis"] = "true",
                            ["RateLimit:RedisConfiguration"] = TestRedis,
                            ["RateLimit:RedisKeyPrefix"] = TestRedisPrefix,
                            ["RateLimit:AuthPermitLimit"] = PermitLimit.ToString(),
                            ["RateLimit:AuthWindowSeconds"] = WindowSeconds.ToString(),
                            ["RateLimit:GlobalPermitLimit"] = "0",
                            ["Outbox:Enabled"] = "false"
                        });
                    });
                });
            return factory;
        }

        _hostA = CreateHost("A");
        _hostB = CreateHost("B");
        _clientA = _hostA.CreateClient();
        _clientB = _hostB.CreateClient();
    }

    /// <summary>套件清理：释放客户端与宿主。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _clientA.Dispose();
        _clientB.Dispose();
        _hostA.Dispose();
        _hostB.Dispose();
    }

    /// <summary>
    /// 重置限流配额：按前缀删除测试 Redis 库中的历史限流键（SCAN+DEL，无需 admin 模式），
    /// 并对齐固定窗口起点——若当前窗口剩余不足 <see cref="MinWindowRemainingSeconds"/> 秒，先等到下一窗口，
    /// 保证整个用例的计数不会被窗口滚动清零。
    /// </summary>
    private static async Task ResetQuotaAsync()
    {
        using var mux = ConnectionMultiplexer.Connect(TestRedis);
        var server = mux.GetServer(mux.GetEndPoints()[0]);
        var db = mux.GetDatabase();

        // Redis 不可用会让限流器 fail-open 放行，此时「未出现 429」不能归因于被测逻辑：前置断言给出明确诊断
        var ping = await db.PingAsync();
        Assert.That(ping.TotalMilliseconds, Is.GreaterThanOrEqualTo(0), "分布式限流集成测试依赖本机 Redis（127.0.0.1:6379）");

        // 窗口起点由限流器取 Redis 服务端 TIME 计算，测试按同一时钟对齐，避免实例间时钟偏差
        var now = new DateTimeOffset(await server.TimeAsync()).ToUnixTimeSeconds();
        var remaining = WindowSeconds - now % WindowSeconds;
        if (remaining < MinWindowRemainingSeconds)
        {
            await Task.Delay(TimeSpan.FromSeconds(remaining + 1));
        }

        foreach (var key in server.Keys(database: 3, pattern: TestRedisPrefix + "*", pageSize: 200))
        {
            db.KeyDelete(key);
        }
    }

    /// <summary>
    /// 核心验收：两个独立宿主共享同一 Redis 配额。
    /// 副本 A 先消耗 4 次配额；副本 B 仅剩 2 次——若计数为进程内（现状缺陷），副本 B 各自重新计数、4 次全部成功，断言失败。
    /// </summary>
    [Test]
    public async Task TwoInstances_ShareAuthQuota_AcrossHosts()
    {
        await ResetQuotaAsync();

        // 副本 A：消耗 4 次配额（均在窗口内）
        for (var i = 0; i < 4; i++)
        {
            var response = await _clientA.GetAsync("/api/auth/captcha");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"宿主 A 第 {i + 1} 次请求应成功");
        }

        // 副本 B：共享配额只剩 2 次——前 2 次成功，后 2 次必须被限流（429）
        var okCount = 0;
        var rejectedCount = 0;
        for (var i = 0; i < 4; i++)
        {
            var response = await _clientB.GetAsync("/api/auth/captcha");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                okCount++;
            }
            else if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejectedCount++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(okCount, Is.EqualTo(2), "宿主 B 在共享配额下只应成功 2 次");
            Assert.That(rejectedCount, Is.EqualTo(2), "宿主 B 超出的 2 次必须返回 429（跨副本共享计数）");
        });
    }

    /// <summary>被限流响应保持统一结构：HTTP 429 + CommonTooManyRequests 错误码 + Retry-After 头。</summary>
    [Test]
    public async Task Rejected_Response_HasUnifiedBody_AndRetryAfter()
    {
        await ResetQuotaAsync();

        // 先消耗满配额
        for (var i = 0; i < PermitLimit; i++)
        {
            var response = await _clientA.GetAsync("/api/auth/captcha");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"预消耗第 {i + 1} 次应成功");
        }

        // 配额已满：下一次请求必须被限流
        var rejected = await _clientB.GetAsync("/api/auth/captcha");
        var body = await rejected.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(rejected.Headers.RetryAfter, Is.Not.Null, "429 响应应携带 Retry-After 头");
            Assert.That(body, Does.Contain("40007"), "响应体应包含 CommonTooManyRequests 错误码");
        });
    }
}
