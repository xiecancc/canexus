using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using API.Middlewares;
using Domain.Entities;
using Domain.Services.Security;
using Domain.Shared.Enums;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Tenants;
using Infrastructure.Tenants;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace API.Tests;
/// <summary>
/// 冷缓存租户校验回归（V-01 / SEC-01）：sys_tenant 是平台级全局表（TenantId IS NULL），
/// 但 TenantResolutionMiddleware 在认证前已把 X-Tenant-Id 写入 ITenantContext，
/// 修复前 TenantAccessValidator 走通用仓储会被自身租户过滤器屏蔽，把存在且启用的租户误判为「不存在」。
/// <para>三条用例分别覆盖匿名分支、平台用户切换租户分支、租户列表查询分支；
/// 每条都使用全新租户 Id 并显式清除 tenant:access 缓存，杜绝缓存预热掩盖缺陷。</para>
/// </summary>
[TestFixture]
public class TenantColdCacheRegressionTests
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static ICache _cache = null!;
    private static string _adminToken = null!;

    /// <summary>启动多租户开启的测试宿主并以平台 admin 登录（登录请求不携带租户头）。</summary>
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
                        ["RateLimit:Enabled"] = "false",
                        ["MultiTenancy:Enabled"] = "true"
                    });
                });
            });

        _httpClient = _factory.CreateClient();
        _db = _factory.Services.GetRequiredService<ISqlSugarClient>();
        _cache = _factory.Services.GetRequiredService<ICache>();
        _adminToken = await LoginAsync(AdminUserName, AdminPassword);
    }

    /// <summary>释放宿主资源。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
        _db.Dispose();
        (_cache as IDisposable)?.Dispose();
        _factory.Dispose();
    }

    /// <summary>避免上一用例的 AsyncLocal 租户上下文污染后续用例。</summary>
    [SetUp]
    public void ResetTenantContext()
    {
        _factory.Services.GetRequiredService<ITenantContext>().TenantId = null;
    }

    /// <summary>匿名请求带头：TenantAuthorizationMiddleware 的匿名分支应校验通过并放行。</summary>
    [Test]
    public async Task Anonymous_ColdTenantHeader_IsAccepted()
    {
        var tenantKey = "v01-anon-" + Guid.NewGuid().ToString("N")[..10];
        var tenantId = await InsertPlatformTenantAsync(tenantKey);

        try
        {
            await InvalidateTenantAccessAsync(tenantId);
            var response = await SendWithTenantHeaderAsync("/api/auth/captcha", tenantId);
            var body = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                $"冷缓存下匿名请求携带启用租户头应通过校验，实际：{body}");
        }
        finally
        {
            DeleteTenant(tenantId);
        }
    }

    /// <summary>平台用户切换租户：带令牌且带头时不应被误判为「租户不存在」。</summary>
    [Test]
    public async Task PlatformAdmin_ColdTenantHeader_IsAccepted()
    {
        var tenantKey = "v01-plat-" + Guid.NewGuid().ToString("N")[..10];
        var tenantId = await InsertPlatformTenantAsync(tenantKey);

        try
        {
            await InvalidateTenantAccessAsync(tenantId);
            var response = await SendWithTenantAsync("/api/auth/user-info", _adminToken, tenantId);
            var body = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                $"冷缓存下平台用户切换启用租户应通过校验，实际：{body}");
        }
        finally
        {
            DeleteTenant(tenantId);
        }
    }

    /// <summary>租户列表：平台级全局表在租户上下文下仍应可见（验证修复不能只覆盖校验器单点）。</summary>
    [Test]
    public async Task PlatformAdmin_TenantPage_WithTenantHeader_ContainsTarget()
    {
        var tenantKey = "v01-page-" + Guid.NewGuid().ToString("N")[..10];
        var tenantId = await InsertPlatformTenantAsync(tenantKey);

        try
        {
            await InvalidateTenantAccessAsync(tenantId);
            var response = await SendWithTenantAsync(
                $"/api/system/tenant/page?keyword={tenantKey}&pageIndex=1&pageSize=20", _adminToken, tenantId);
            var body = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"租户分页应成功，实际：{body}");
            using var json = JsonDocument.Parse(body);
            var items = json.RootElement.GetProperty("data").GetProperty("items");
            var keys = items.EnumerateArray()
                .Select(item => item.GetProperty("tenantKey").GetString())
                .ToList();
            Assert.That(keys, Does.Contain(tenantKey),
                "sys_tenant 为平台级全局表，携带租户头时租户列表仍应包含目标租户");
        }
        finally
        {
            DeleteTenant(tenantId);
        }
    }

    /// <summary>直插平台级启用租户（TenantId 由 Aop 按当前 null 上下文填充，保持 IS NULL）。</summary>
    private async Task<Guid> InsertPlatformTenantAsync(string tenantKey)
    {
        var tenant = new SysTenant
        {
            TenantKey = tenantKey,
            TenantName = "冷缓存回归租户",
            Status = CommonStatus.Enabled,
            ExpireTime = DateTime.UtcNow.AddYears(10)
        };
        await _db.Insertable(tenant).ExecuteCommandAsync();
        return tenant.Id;
    }

    /// <summary>清除该租户的访问状态缓存，确保每次都是真正的冷缓存。</summary>
    private async Task InvalidateTenantAccessAsync(Guid tenantId)
        => await _cache.RemoveAsync(TenantAccessValidator.CacheKeyPrefix + tenantId);

    /// <summary>清理测试租户行。</summary>
    private void DeleteTenant(Guid tenantId)
        => _db.Deleteable<SysTenant>().Where(t => t.Id == tenantId).ExecuteCommand();

    /// <summary>平台 admin 登录（不带租户头），返回访问令牌。</summary>
    private async Task<string> LoginAsync(string userName, string password)
    {
        var captchaResponse = await _httpClient.GetAsync("/api/auth/captcha");
        captchaResponse.EnsureSuccessStatusCode();
        using var captchaBody = JsonDocument.Parse(await captchaResponse.Content.ReadAsStringAsync());
        var captchaId = captchaBody.RootElement.GetProperty("data").GetProperty("captchaId").GetString()!;
        var captchaCode = await _cache.GetAsync<string>(ICaptchaService.CacheKeyPrefix + captchaId);
        Assert.That(captchaCode, Is.Not.Null, "验证码应已写入缓存");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { userName, password, captchaId, captchaCode })
        };
        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True, $"登录应成功：{content}");
        using var body = JsonDocument.Parse(content);
        return body.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    /// <summary>只带租户头的匿名 GET 请求。</summary>
    private static async Task<HttpResponseMessage> SendWithTenantHeaderAsync(string url, Guid tenantId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(TenantResolutionMiddleware.TenantHeader, tenantId.ToString());
        return await _httpClient.SendAsync(request);
    }

    /// <summary>携带令牌与租户头的 GET 请求。</summary>
    private static async Task<HttpResponseMessage> SendWithTenantAsync(string url, string accessToken, Guid tenantId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add(TenantResolutionMiddleware.TenantHeader, tenantId.ToString());
        return await _httpClient.SendAsync(request);
    }
}
