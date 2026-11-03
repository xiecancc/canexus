
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using Domain.Entities;
using Domain.Services.Security;
using Domain.Shared.Enums;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace API.Tests;
/// <summary>
/// 多租户（B9）集成测试——多租户关闭（默认配置）：
/// 走真实 HTTP 管道 + 真实 MySQL（默认库 super_admin_test，SUPER_ADMIN_TEST_CONN 可覆盖），
/// 重点验证租户 CRUD、唯一性 / 不可变性 / 归属用户删除拒绝，
/// 以及 Enabled=false 时即使携带 X-Tenant-Id 头也不过滤数据。
/// </summary>
[TestFixture]
public class TenantApiTests
{
    /// <summary>默认测试连接串（root 密码为本机约定值）。</summary>
    private const string DefaultConnectionString =
        "server=localhost;port=3306;database=super_admin_test;user id=root;password=***REMOVED***;CharSet=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=none";

    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static ICache _cache = null!;
    private static string _adminToken = null!;

    /// <summary>测试套件初始化：启动测试宿主并准备种子数据。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("SUPER_ADMIN_TEST_CONN") ?? DefaultConnectionString;
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

    // —— 租户管理 CRUD ——/

    /// <summary>验证租户全流程：创建、重复键拒绝、tenantKey 不可变更、存在归属用户时禁止删除。</summary>
    [Test]
    public async Task Tenant_FullFlow_DuplicateAndImmutableAndInUseDelete()
    {
        var tenantKey = NewTenantKey();

        // 创建 + 分页字段校验
        var createBody = await SendForJsonAsync(HttpMethod.Post, "/api/system/tenant", new
        {
            tenantKey,
            tenantName = "B9 流程租户",
            contactPhone = "13800000001",
            status = 1,
            description = "B9 集成测试",
            expireTime = (DateTime?)new DateTime(2030, 1, 1)
        });
        var tenantId = await ApiTestSupport.GetEntityIdAsync<SysTenant>(_db, it => it.TenantKey == tenantKey);

        var page = await GetJsonAsync($"/api/system/tenant/page?keyword={tenantKey}");
        var row = page.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("id").GetGuid(), Is.EqualTo(tenantId));
            Assert.That(row.GetProperty("tenantKey").GetString(), Is.EqualTo(tenantKey));
            Assert.That(row.GetProperty("tenantName").GetString(), Is.EqualTo("B9 流程租户"));
            Assert.That(row.GetProperty("status").GetInt32(), Is.EqualTo(1));
            Assert.That(row.TryGetProperty("createdAt", out _), Is.True, "列表应包含 createdAt");
        });

        // 重复 tenantKey → 400802
        var duplicate = await SendAsync(HttpMethod.Post, "/api/system/tenant", new
        {
            tenantKey,
            tenantName = "重复租户",
            status = 1
        });
        var duplicateBody = await ReadBodyAsync(duplicate);
        Assert.Multiple(() =>
        {
            Assert.That(duplicate.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(duplicateBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400802));
        });

        // 更新（tenantKey 不变）→ 成功（乐观锁：版本号经路由传入）
        var tenantVersion = await ApiTestSupport.GetVersionAsync<SysTenant>(_db, tenantId);
        await SendForJsonAsync(HttpMethod.Put, $"/api/system/tenant/{tenantId}/{tenantVersion}", new
        {
            tenantKey,
            tenantName = "B9 流程租户V2",
            contactPhone = (string?)null,
            status = 1,
            description = "更新后",
            expireTime = (DateTime?)null
        });
        var updated = (await GetJsonAsync($"/api/system/tenant/page?keyword={tenantKey}"))
            .RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.That(updated.GetProperty("tenantName").GetString(), Is.EqualTo("B9 流程租户V2"));

        // 更新变更 tenantKey → 400803（不可变更）
        var immutable = await SendAsync(HttpMethod.Put, $"/api/system/tenant/{tenantId}/{tenantVersion}", new
        {
            tenantKey = tenantKey + "x",
            tenantName = "B9 流程租户V2",
            status = 1
        });
        var immutableBody = await ReadBodyAsync(immutable);
        Assert.Multiple(() =>
        {
            Assert.That(immutable.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(immutableBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400803));
        });

        // GET all：启用项简易列表包含新建租户
        var all = await GetJsonAsync("/api/system/tenant/all");
        var allKeys = all.RootElement.GetProperty("data").EnumerateArray()
            .Select(t => t.GetProperty("tenantKey").GetString()).ToList();
        Assert.That(allKeys, Does.Contain(tenantKey), "启用租户简易列表应包含新建租户");

        // 存在归属用户时删除 → 400804
        var userName = NewUserName();
        await InsertTenantUserAsync(userName, tenantId);
        var inUse = await SendAsync(HttpMethod.Delete, $"/api/system/tenant/{tenantId}/{await ApiTestSupport.GetVersionAsync<SysTenant>(_db, tenantId)}");
        var inUseBody = await ReadBodyAsync(inUse);
        Assert.Multiple(() =>
        {
            Assert.That(inUse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(inUseBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400804));
        });

        // 清理归属用户后租户可删除
        RemoveTenantUser(userName);
        var deleted = await SendAsync(HttpMethod.Delete, $"/api/system/tenant/{tenantId}/{await ApiTestSupport.GetVersionAsync<SysTenant>(_db, tenantId)}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.OK), "无归属用户的租户应可删除");
    }

    // —— 多租户关闭：携带租户头也不过滤 ——/

    /// <summary>验证多租户关闭时携带租户头也不过滤数据。</summary>
    [Test]
    public async Task UserPage_WhenTenancyDisabled_TenantHeaderIgnored()
    {
        var token = NewUserName()[..10];
        var userNameA = "a_" + token;
        var userNameB = "b_" + token;
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await InsertTenantUserAsync(userNameA, tenantA);
        await InsertTenantUserAsync(userNameB, tenantB);

        try
        {
            // 带租户头请求：MultiTenancy:Enabled=false 时中间件不读取该头，应不过滤（两个租户的数据均可见）
            var withHeader = await GetJsonAsync($"/api/system/user/page?keyword={token}", tenantId: tenantA.ToString());
            var withHeaderItems = withHeader.RootElement.GetProperty("data").GetProperty("items")
                .EnumerateArray().Select(u => u.GetProperty("userName").GetString()).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(withHeaderItems, Does.Contain(userNameA), "关闭多租户时应可见 tenant-a 用户");
                Assert.That(withHeaderItems, Does.Contain(userNameB), "关闭多租户时携带租户头不应过滤数据");
            });

            // 不带头请求同样全部可见
            var withoutHeader = await GetJsonAsync($"/api/system/user/page?keyword={token}");
            var withoutHeaderItems = withoutHeader.RootElement.GetProperty("data").GetProperty("items")
                .EnumerateArray().Select(u => u.GetProperty("userName").GetString()).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(withoutHeaderItems, Does.Contain(userNameA));
                Assert.That(withoutHeaderItems, Does.Contain(userNameB));
            });
        }
        finally
        {
            RemoveTenantUser(userNameA);
            RemoveTenantUser(userNameB);
        }
    }

    // —— 测试辅助 ——

    /// <summary>生成唯一租户标识。</summary>
    private static string NewTenantKey() => "b9_tenant_" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>生成唯一用户名。</summary>
    private static string NewUserName() => "b9" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>直接写库插入指定租户的测试用户（绕过 API，租户归属由 TenantId 表达）。</summary>
    private async Task InsertTenantUserAsync(string userName, Guid tenantId)
    {
        await _db.Insertable(new SysUser
        {
            UserName = userName,
            NickName = "B9 租户用户",
            PasswordHash = "test",
            TenantId = tenantId,
            Status = CommonStatus.Enabled
        }).ExecuteCommandAsync();
    }

    /// <summary>按用户名物理删除测试用户。</summary>
    private void RemoveTenantUser(string userName)
    {
        _db.Deleteable<SysUser>().Where(u => u.UserName == userName).ExecuteCommand();
    }

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

    /// <summary>发送 JSON 请求（可附带租户头）。</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? payload = null, string? tenantId = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = payload is null ? null : JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        if (tenantId is not null)
        {
            request.Headers.Add("X-Tenant-Id", tenantId);
        }

        return await _httpClient.SendAsync(request);
    }

    /// <summary>发送请求并把响应解析为 JsonDocument（断言成功状态）。</summary>
    private static async Task<JsonDocument> SendForJsonAsync(HttpMethod method, string url, object payload, string? tenantId = null)
    {
        var response = await SendAsync(method, url, payload, tenantId);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True, $"请求应成功：{method} {url}，响应：{content}");
        return JsonDocument.Parse(content);
    }

    /// <summary>GET 并解析响应（断言成功状态）。</summary>
    private static async Task<JsonDocument> GetJsonAsync(string url, string? tenantId = null)
    {
        return await SendForJsonAsync(HttpMethod.Get, url, null!, tenantId);
    }

    /// <summary>读取响应体为 JsonDocument。</summary>
    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}

/// <summary>
/// 多租户（B9）集成测试——多租户启用（内存配置覆盖 MultiTenancy:Enabled=true）：
/// 租户 A 请求仅可见归属租户 A 的用户，平台请求（不带租户头）可见全部。
/// </summary>
[TestFixture]
public class TenantEnabledApiTests
{
    /// <summary>默认测试连接串（root 密码为本机约定值）。</summary>
    private const string DefaultConnectionString =
        "server=localhost;port=3306;database=super_admin_test;user id=root;password=***REMOVED***;CharSet=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=none";

    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-00000000a001");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-00000000b002");

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static ICache _cache = null!;
    private static string _adminToken = null!;

    /// <summary>测试套件初始化：启动测试宿主并启用多租户开关。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("SUPER_ADMIN_TEST_CONN") ?? DefaultConnectionString;
        var apiContentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "API"));
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // 显式加载 API 项目的 appsettings.json，并以内存配置覆盖连接串与多租户开关（内存配置后注册优先级高；UseSetting 会被 appsettings.json 覆盖而失效）
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddJsonFile(Path.Combine(apiContentRoot, "appsettings.json"), optional: false, reloadOnChange: false);
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = connectionString,
                        ["Jwt:Secret"] = ApiTestSupport.TestJwtSecret,
                        ["MultiTenancy:Enabled"] = "true",
                        ["RateLimit:Enabled"] = "false" // 集成测试共享 127.0.0.1 分区且高频请求，关闭限流避免不稳定
                    });
                });
            });

        _httpClient = _factory.CreateClient();
        _db = _factory.Services.GetRequiredService<ISqlSugarClient>();
        _cache = _factory.Services.GetRequiredService<ICache>();
        _adminToken = await LoginAndGetAccessTokenAsync("admin", "admin123");
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

    /// <summary>验证多租户开启时按租户头过滤并隔离各租户数据。</summary>
    [Test]
    public async Task UserPage_WhenTenancyEnabled_TenantFilterIsolatesData()
    {
        var token = Guid.NewGuid().ToString("N")[..10];
        var userNameA = "a_" + token;
        var userNameB = "b_" + token;
        await InsertTenantUserAsync(userNameA, TenantA);
        await InsertTenantUserAsync(userNameB, TenantB);

        try
        {
            // 租户 A 请求：仅可见归属租户 A 的用户
            var tenantAView = await GetJsonAsync($"/api/system/user/page?keyword={token}", tenantId: TenantA.ToString());
            var tenantAItems = tenantAView.RootElement.GetProperty("data").GetProperty("items")
                .EnumerateArray().Select(u => u.GetProperty("userName").GetString()).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(tenantAItems, Does.Contain(userNameA), "租户 A 应可见归属租户 A 的用户");
                Assert.That(tenantAItems, Does.Not.Contain(userNameB), "租户 A 不可见归属租户 B 的用户");
            });

            // 租户 B 请求：仅可见归属租户 B 的用户
            var tenantBView = await GetJsonAsync($"/api/system/user/page?keyword={token}", tenantId: TenantB.ToString());
            var tenantBItems = tenantBView.RootElement.GetProperty("data").GetProperty("items")
                .EnumerateArray().Select(u => u.GetProperty("userName").GetString()).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(tenantBItems, Does.Contain(userNameB), "租户 B 应可见归属租户 B 的用户");
                Assert.That(tenantBItems, Does.Not.Contain(userNameA), "租户 B 不可见归属租户 A 的用户");
            });

            // 平台请求（不带租户头）：不过滤，两个租户的数据均可见
            var platformView = await GetJsonAsync($"/api/system/user/page?keyword={token}");
            var platformItems = platformView.RootElement.GetProperty("data").GetProperty("items")
                .EnumerateArray().Select(u => u.GetProperty("userName").GetString()).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(platformItems, Does.Contain(userNameA), "平台视图应可见租户 A 用户");
                Assert.That(platformItems, Does.Contain(userNameB), "平台视图应可见租户 B 用户");
            });
        }
        finally
        {
            _db.Deleteable<SysUser>().Where(u => u.UserName == userNameA || u.UserName == userNameB).ExecuteCommand();
        }
    }

    // —— 测试辅助 ——

    /// <summary>直接写库插入指定租户的测试用户。</summary>
    private async Task InsertTenantUserAsync(string userName, Guid tenantId)
    {
        await _db.Insertable(new SysUser
        {
            UserName = userName,
            NickName = "B9 启用租户用户",
            PasswordHash = "test",
            TenantId = tenantId,
            Status = CommonStatus.Enabled
        }).ExecuteCommandAsync();
    }

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

    /// <summary>发送 JSON 请求（可附带租户头）。</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? payload = null, string? tenantId = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = payload is null ? null : JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        if (tenantId is not null)
        {
            request.Headers.Add("X-Tenant-Id", tenantId);
        }

        return await _httpClient.SendAsync(request);
    }

    /// <summary>发送请求并把响应解析为 JsonDocument（断言成功状态）。</summary>
    private static async Task<JsonDocument> SendForJsonAsync(HttpMethod method, string url, object payload, string? tenantId = null)
    {
        var response = await SendAsync(method, url, payload, tenantId);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True, $"请求应成功：{method} {url}，响应：{content}");
        return JsonDocument.Parse(content);
    }

    /// <summary>GET 并解析响应（断言成功状态）。</summary>
    private static async Task<JsonDocument> GetJsonAsync(string url, string? tenantId = null)
    {
        return await SendForJsonAsync(HttpMethod.Get, url, null!, tenantId);
    }

    /// <summary>读取响应体为 JsonDocument。</summary>
    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
