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
/// P2 功能补齐集成测试（角色独立启停 / Excel 导出 / 缓存监控）：
/// 走真实 HTTP 管道 + 真实 MySQL，验证角色启停端到端语义、
/// 用户导出返回合法 xlsx（PK 魔数）、缓存监控端点降级输出。
/// </summary>
[TestFixture]
public class FeatureGapApiTests
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static string _adminToken = null!;

    /// <summary>测试套件初始化：启动测试宿主并走真实登录链路获取令牌。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = ApiTestSupport.TestConnectionString;
        var apiContentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "API"));
        _factory = new ApiTestFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddJsonFile(Path.Combine(apiContentRoot, "appsettings.json"), optional: false, reloadOnChange: false);
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = connectionString,
                        ["Jwt:Secret"] = ApiTestSupport.TestJwtSecret,
                        ["RateLimit:Enabled"] = "false"
                    });
                });
            });

        _httpClient = _factory.CreateClient();
        _db = _factory.Services.GetRequiredService<ISqlSugarClient>();

        var captcha = await _httpClient.GetAsync("/api/auth/captcha");
        captcha.EnsureSuccessStatusCode();
        var captchaBody = await ReadBodyAsync(captcha);
        var captchaId = captchaBody.RootElement.GetProperty("data").GetProperty("captchaId").GetString()!;
        var cache = _factory.Services.GetRequiredService<ICache>();
        var code = await cache.GetAsync<string>(ICaptchaService.CacheKeyPrefix + captchaId);
        Assert.That(code, Is.Not.Null, "验证码应已写入缓存");

        var login = await _httpClient.PostAsJsonAsync("/api/auth/login", new
        {
            userName = AdminUserName,
            password = AdminPassword,
            captchaId,
            captchaCode = code
        });
        var loginBody = await ReadBodyAsync(login);
        Assert.That(login.IsSuccessStatusCode, Is.True, $"admin 登录应成功：{loginBody.RootElement.GetRawText()}");
        _adminToken = loginBody.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    /// <summary>测试套件清理：释放宿主与资源。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
        _db.Dispose();
        _factory.Dispose();
    }

    // —— 角色独立启停 ——

    /// <summary>验证角色独立启停：停用后详情状态为 0，再次启用恢复 1；super_admin 启停被拒绝（400304）。</summary>
    [Test]
    public async Task Role_UpdateStatus_Toggle_And_SuperAdminProtected()
    {
        // 创建测试角色（dataScope=1 全部，无菜单授权）
        var roleKey = "r" + Guid.NewGuid().ToString("N")[..10];
        await SendForJsonAsync(HttpMethod.Post, "/api/system/role", new
        {
            roleName = "启停测试角色",
            roleKey,
            dataScope = 1,
            status = 1,
            menuIds = Array.Empty<Guid>()
        });
        var roleId = await ApiTestSupport.GetEntityIdAsync<SysRole>(_db, it => it.RoleKey == roleKey);

        // 停用 → 详情状态 0
        var version = await ApiTestSupport.GetVersionAsync<SysRole>(_db, roleId);
        var disable = await SendAsync(HttpMethod.Put, $"/api/system/role/{roleId}/{version}/status", new { status = 0 });
        Assert.That(disable.IsSuccessStatusCode, Is.True, $"停用应成功：{(await disable.Content.ReadAsStringAsync())}");
        var afterDisable = await GetJsonAsync($"/api/system/role/{roleId}");
        Assert.That(afterDisable.RootElement.GetProperty("data").GetProperty("status").GetInt32(), Is.EqualTo(0));

        // 启用 → 详情状态 1
        var versionAfterDisable = await ApiTestSupport.GetVersionAsync<SysRole>(_db, roleId);
        await SendAsync(HttpMethod.Put, $"/api/system/role/{roleId}/{versionAfterDisable}/status", new { status = 1 });
        var afterEnable = await GetJsonAsync($"/api/system/role/{roleId}");
        Assert.That(afterEnable.RootElement.GetProperty("data").GetProperty("status").GetInt32(), Is.EqualTo(1));

        // super_admin 启停被拒绝（内置超管角色保护）
        var superAdminId = (await _db.Queryable<SysRole>().FirstAsync(it => it.RoleKey == "super_admin")).Id;
        var superAdminVersion = await ApiTestSupport.GetVersionAsync<SysRole>(_db, superAdminId);
        var reject = await SendAsync(HttpMethod.Put, $"/api/system/role/{superAdminId}/{superAdminVersion}/status", new { status = 0 });
        var rejectBody = await ReadBodyAsync(reject);
        Assert.That(rejectBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400304), "super_admin 启停应返回 400304");

        // 清理测试角色
        await SendAsync(HttpMethod.Delete, $"/api/system/role/{roleId}/{await ApiTestSupport.GetVersionAsync<SysRole>(_db, roleId)}");
    }

    // —— Excel 导出 ——

    /// <summary>验证用户导出返回合法 xlsx（PK 魔数 + xlsx MIME + 建议文件名）。</summary>
    [Test]
    public async Task User_Export_ReturnsValidXlsx()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/system/user/export");
        Assert.That(response.IsSuccessStatusCode, Is.True, "用户导出应成功");
        Assert.That(response.Content.Headers.ContentType?.MediaType,
            Is.EqualTo("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"), "应为 xlsx MIME");
        Assert.That(response.Content.Headers.ContentDisposition?.FileNameStar,
            Does.StartWith("用户列表_"), "建议文件名应以「用户列表_」开头");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length, Is.GreaterThan(4), "xlsx 内容不应为空");
            Assert.That(bytes[0], Is.EqualTo((byte)'P'), "xlsx（zip 容器）应以 PK 魔数开头");
            Assert.That(bytes[1], Is.EqualTo((byte)'K'), "xlsx（zip 容器）应以 PK 魔数开头");
        });
    }

    /// <summary>验证操作日志导出与登录日志导出均返回合法 xlsx。</summary>
    [Test]
    public async Task Log_Export_ReturnsValidXlsx()
    {
        var operLog = await SendAsync(HttpMethod.Get, "/api/system/oper-log/export");
        Assert.That(operLog.IsSuccessStatusCode, Is.True,
            $"操作日志导出应成功：{await operLog.Content.ReadAsStringAsync()}");
        var operLogBytes = await operLog.Content.ReadAsByteArrayAsync();
        Assert.That(operLogBytes.Length, Is.GreaterThan(4), "操作日志导出内容不应为空");

        var loginLog = await SendAsync(HttpMethod.Get, "/api/system/login-log/export");
        Assert.That(loginLog.IsSuccessStatusCode, Is.True, "登录日志导出应成功");
        var loginLogBytes = await loginLog.Content.ReadAsByteArrayAsync();
        Assert.That(loginLogBytes.Length, Is.GreaterThan(4), "登录日志导出内容不应为空");
    }

    // —— 缓存监控 ——

    /// <summary>验证缓存监控端点返回配置快照（测试环境 RedisEnabled=false → RedisConnected 为 null）。</summary>
    [Test]
    public async Task Cache_Monitor_ReturnsSnapshot()
    {
        var body = await GetJsonAsync("/api/monitor/cache");
        var data = body.RootElement.GetProperty("data");
        Assert.Multiple(() =>
        {
            Assert.That(data.GetProperty("redisEnabled").GetBoolean(), Is.False, "测试环境未启用 Redis");
            Assert.That(data.TryGetProperty("redisConnected", out var redisConnected)
                && redisConnected.ValueKind == JsonValueKind.Null, Is.True, "未启用 Redis 时 redisConnected 应为 null");
            Assert.That(data.GetProperty("ttl").GetProperty("hot").GetProperty("localSeconds").GetInt32(), Is.GreaterThan(0), "TTL 档位快照应输出");
        });
    }

    // —— 测试辅助 ——

    /// <summary>发送 JSON 请求（POST / PUT / DELETE 等）。</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = payload is null ? null : JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        return await _httpClient.SendAsync(request);
    }

    /// <summary>发送请求并把响应解析为 JsonDocument（断言成功状态）。</summary>
    private static async Task<JsonDocument> SendForJsonAsync(HttpMethod method, string url, object payload)
    {
        var response = await SendAsync(method, url, payload);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True, $"请求应成功：{method} {url}，响应：{content}");
        return JsonDocument.Parse(content);
    }

    /// <summary>GET 并解析响应（断言成功状态）。</summary>
    private static async Task<JsonDocument> GetJsonAsync(string url)
    {
        return await SendForJsonAsync(HttpMethod.Get, url, null!);
    }

    /// <summary>读取响应体为 JsonDocument。</summary>
    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
