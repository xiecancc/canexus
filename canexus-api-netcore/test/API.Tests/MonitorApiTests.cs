
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using Domain.Entities;
using Domain.Services.Security;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace API.Tests;
/// <summary>
/// 系统监控（服务器运行信息）集成测试：
/// 走真实 HTTP 管道 + 真实 MySQL（super_admin_test，SUPER_ADMIN_TEST_CONN 可覆盖），
/// 重点验证运行信息快照字段完整性（主机 / 运行时 / 内存 / 调度器概要）与不含敏感配置值。
/// </summary>
[TestFixture]
public class MonitorApiTests
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static ICache _cache = null!;
    private static string _adminToken = null!;

    /// <summary>测试套件初始化：启动测试宿主并以 admin 登录。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = ApiTestSupport.TestConnectionString;
        var apiContentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "API"));
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // 显式加载 API 项目的 appsettings.json，并以内存配置覆盖连接串（内存配置后注册优先级高；UseSetting 会被 appsettings.json 覆盖而失效）
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddJsonFile(Path.Combine(apiContentRoot, "appsettings.json"), optional: false, reloadOnChange: false);
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = connectionString,
                        ["Jwt:Secret"] = ApiTestSupport.TestJwtSecret,
                        ["RateLimit:Enabled"] = "false" // 集成测试共享 127.0.0.1 分区且高频请求，关闭限流避免不稳定
                    });
                });
            });

        _httpClient = _factory.CreateClient();
        _db = _factory.Services.GetRequiredService<ISqlSugarClient>();
        _cache = _factory.Services.GetRequiredService<ICache>();

        _adminToken = await LoginAndGetAccessTokenAsync(AdminUserName, AdminPassword);
    }

    /// <summary>测试套件清理：释放宿主与资源。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
        _db.Dispose();
        (_cache as IDisposable)?.Dispose();
        _factory.Dispose();
    }

    /// <summary>验证服务器运行信息快照：字段完整、数值合理、调度器已启动，且不含连接串等敏感配置。</summary>
    [Test]
    public async Task Monitor_Server_ReturnsRuntimeSnapshot()
    {
        var response = await GetJsonAsync("/api/monitor/server");
        var data = response.RootElement.GetProperty("data");

        Assert.Multiple(() =>
        {
            Assert.That(response.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(200));
            Assert.That(data.GetProperty("machineName").GetString(), Is.Not.Empty, "应返回主机名");
            Assert.That(data.GetProperty("osDescription").GetString(), Is.Not.Empty, "应返回操作系统描述");
            Assert.That(data.GetProperty("frameworkDescription").GetString(), Does.StartWith(".NET"), "应返回运行时框架描述");
            Assert.That(data.GetProperty("processorCount").GetInt32(), Is.GreaterThanOrEqualTo(1), "CPU 核心数应至少为 1");
            Assert.That(data.GetProperty("uptimeSeconds").GetDouble(), Is.GreaterThan(0), "进程运行时长应大于 0");
            Assert.That(data.GetProperty("workingSetMb").GetDouble(), Is.GreaterThan(0), "工作集内存应大于 0");
            Assert.That(data.GetProperty("gcHeapMb").GetDouble(), Is.GreaterThanOrEqualTo(0));
            Assert.That(data.GetProperty("threadCount").GetInt32(), Is.GreaterThan(0), "线程数应大于 0");
            Assert.That(data.GetProperty("serverTimeUtc").GetDateTime(), Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(1)), "服务器时间应为当前 UTC 时间附近");
        });

        var scheduler = data.GetProperty("scheduler");
        Assert.Multiple(() =>
        {
            Assert.That(scheduler.GetProperty("isStarted").GetBoolean(), Is.True, "测试宿主启动后调度器应为已启动状态");
            Assert.That(scheduler.GetProperty("runningJobCount").GetInt32(), Is.GreaterThanOrEqualTo(0));
            Assert.That(scheduler.GetProperty("totalJobCount").GetInt32(), Is.GreaterThanOrEqualTo(0));
        });

        // 敏感信息防护：响应体不得包含连接串片段与 JWT 密钥
        var raw = response.RootElement.GetRawText();
        Assert.Multiple(() =>
        {
            Assert.That(raw, Does.Not.Contain("super_admin_test"), "响应不得包含数据库名");
            Assert.That(raw, Does.Not.Contain("password="), "响应不得包含连接串口令片段");
            Assert.That(raw, Does.Not.Contain(ApiTestSupport.TestJwtSecret), "响应不得包含 JWT 密钥");
        });
    }

    // —— 测试辅助 ——

    /// <summary>调用验证码接口并从 IMemoryCache 读取明文验证码。</summary>
    private async Task<(string CaptchaId, string CaptchaCode)> GetCaptchaAsync()
    {
        var response = await _httpClient.GetAsync("/api/auth/captcha");
        response.EnsureSuccessStatusCode();
        var body = await ReadBodyAsync(response);
        var captchaId = body.RootElement.GetProperty("data").GetProperty("captchaId").GetString()!;
        var code = await _cache.GetAsync<string>(ICaptchaService.CacheKeyPrefix + captchaId);
        Assert.That(code, Is.Not.Null, "验证码应已写入缓存");
        return (captchaId, code!);
    }

    /// <summary>登录并返回 accessToken。</summary>
    private async Task<string> LoginAndGetAccessTokenAsync(string userName, string password)
    {
        var (captchaId, captchaCode) = await GetCaptchaAsync();
        var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new
        {
            userName,
            password,
            captchaId,
            captchaCode
        });
        var body = await ReadBodyAsync(response);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"登录应成功：{body.RootElement.GetRawText()}");
        return body.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    /// <summary>GET 并解析响应（断言成功状态，携带 admin 令牌）。</summary>
    private static async Task<JsonDocument> GetJsonAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True, $"请求应成功：{url}，响应：{content}");
        return JsonDocument.Parse(content);
    }

    /// <summary>读取响应体为 JsonDocument。</summary>
    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
