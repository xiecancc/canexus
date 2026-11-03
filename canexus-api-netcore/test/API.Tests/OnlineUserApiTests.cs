
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using Domain.Entities;
using Domain.Services.Security;
using Domain.Shared.Results;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace API.Tests;
/// <summary>
/// 在线会话管理（监控模块）集成测试：
/// 走真实 HTTP 管道 + 真实 MySQL（super_admin_test，SUPER_ADMIN_TEST_CONN 可覆盖），
/// 重点验证活跃 refreshToken 会话的分页查询（含用户 / 部门信息组装）、
/// 强制下线吊销会话（后续刷新令牌被拒）与会话不存在时的业务错误。
/// </summary>
[TestFixture]
public class OnlineUserApiTests
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

        _adminToken = (await LoginAsync(AdminUserName, AdminPassword)).AccessToken;
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

    // —— 在线会话查询 ——

    /// <summary>验证登录产生的活跃会话出现在在线列表，且用户 / 部门 / IP / 时间信息正确组装。</summary>
    [Test]
    public async Task Online_Page_ContainsSessionWithUserInfo()
    {
        var page = await GetJsonAsync("/api/monitor/online/page?userName=admin&pageSize=100");
        var data = page.RootElement.GetProperty("data");
        var items = data.GetProperty("items").EnumerateArray().ToList();

        Assert.That(items, Is.Not.Empty, "admin 登录后应有活跃在线会话");
        var row = items[^1];
        Assert.Multiple(() =>
        {
            Assert.That(page.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(200));
            Assert.That(data.GetProperty("total").GetInt64(), Is.GreaterThanOrEqualTo(1));
            Assert.That(row.GetProperty("userName").GetString(), Is.EqualTo(AdminUserName));
            Assert.That(row.GetProperty("nickName").GetString(), Is.Not.Empty, "应组装用户昵称");
            Assert.That(row.TryGetProperty("ip", out _), Is.True, "应返回签发 IP 字段（测试内存服务器的 RemoteIpAddress 为 null，不校验具体值）");
            Assert.That(row.TryGetProperty("loginTime", out _), Is.True, "应返回登录（签发）时间");
            Assert.That(row.TryGetProperty("expiresAt", out _), Is.True, "应返回会话过期时间");
            Assert.That(row.GetProperty("expiresAt").GetDateTime(), Is.GreaterThan(DateTime.UtcNow), "活跃会话的过期时间应晚于当前时间");
        });
    }

    /// <summary>验证按不存在的用户名过滤返回空页；IP 过滤命中指定来源的会话。</summary>
    [Test]
    public async Task Online_Page_FiltersByUserNameAndIp()
    {
        var empty = await GetJsonAsync("/api/monitor/online/page?userName=no_such_user_xyz&pageSize=10");
        Assert.Multiple(() =>
        {
            Assert.That(empty.RootElement.GetProperty("data").GetProperty("total").GetInt64(), Is.EqualTo(0), "不存在的用户名应返回空页");
            Assert.That(empty.RootElement.GetProperty("data").GetProperty("items").GetArrayLength(), Is.EqualTo(0));
        });

        // 测试内存服务器的 RemoteIpAddress 为 null，无法经登录产生带 IP 的会话，改为直插带 IP 的令牌行验证过滤
        var adminId = (await _db.Queryable<SysUser>().Where(u => u.UserName == AdminUserName).FirstAsync()).Id;
        var marked = new SysRefreshToken
        {
            Token = "ipfilter" + Guid.NewGuid().ToString("N"),
            UserId = adminId,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Ip = "10.20.30.40"
        };
        await _db.Insertable(marked).ExecuteCommandAsync();
        try
        {
            var byIp = await GetJsonAsync("/api/monitor/online/page?ip=10.20.30&pageSize=10");
            var matched = byIp.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
                .Any(i => i.GetProperty("id").GetGuid() == marked.Id);
            Assert.That(matched, Is.True, "IP 模糊过滤应命中指定来源的会话");

            var byNoIp = await GetJsonAsync("/api/monitor/online/page?ip=192.168.99&pageSize=10");
            Assert.That(byNoIp.RootElement.GetProperty("data").GetProperty("total").GetInt64(), Is.EqualTo(0), "不匹配的 IP 过滤不应命中");
        }
        finally
        {
            // 清理：吊销测试令牌行，避免残留进其他断言的会话列表
            await _db.Updateable<SysRefreshToken>()
                .SetColumns(t => t.RevokedAt == DateTime.UtcNow)
                .Where(t => t.Id == marked.Id)
                .ExecuteCommandAsync();
        }
    }

    // —— 强制下线 ——

    /// <summary>验证强退吊销会话：列表移除、refreshToken 刷新被拒、重复强退返回会话不存在。</summary>
    [Test]
    public async Task Online_ForceLogout_RevokesSessionAndBlocksRefresh()
    {
        // 独立登录产生一条可强退的会话（不占用套件级 _adminToken）；按令牌串精确定位记录（并发夹具下不能按最新时间猜）
        var pair = await LoginAsync(AdminUserName, AdminPassword);
        var token = await _db.Queryable<SysRefreshToken>()
            .Where(t => t.Token == pair.RefreshToken && t.RevokedAt == null)
            .FirstAsync();
        Assert.That(token, Is.Not.Null, "登录后应有活跃 refreshToken 记录");

        // 强退成功
        var revoke = await SendAsync(HttpMethod.Post, $"/api/monitor/online/{token.Id}/revoke", null);
        var revokeBody = await ReadBodyAsync(revoke);
        Assert.Multiple(() =>
        {
            Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"强退应成功：{revokeBody.RootElement.GetRawText()}");
            Assert.That(revokeBody.RootElement.GetProperty("message").GetString(), Does.Contain("强制下线"));
        });

        // 列表中不再出现该会话
        var page = await GetJsonAsync("/api/monitor/online/page?userName=admin&pageSize=100");
        var stillListed = page.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Any(i => i.GetProperty("id").GetGuid() == token.Id);
        Assert.That(stillListed, Is.False, "强退后会话应从在线列表移除");

        // 被吊销的 refreshToken 无法再刷新令牌（40106）
        var refresh = await _httpClient.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = pair.RefreshToken });
        var refreshBody = await ReadBodyAsync(refresh);
        Assert.Multiple(() =>
        {
            Assert.That(refresh.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "已吊销会话刷新令牌应被拒绝");
            Assert.That(refreshBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(ErrorCodes.AuthRefreshTokenInvalid));
        });

        // 重复强退：会话已下线，返回业务错误 401101
        var again = await SendAsync(HttpMethod.Post, $"/api/monitor/online/{token.Id}/revoke", null);
        var againBody = await ReadBodyAsync(again);
        Assert.Multiple(() =>
        {
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(againBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(ErrorCodes.OnlineSessionNotFound));
        });
    }

    /// <summary>验证强退不存在的会话 Id 返回业务错误 401101。</summary>
    [Test]
    public async Task Online_ForceLogout_NotFoundSession_ReturnsBusinessError()
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/monitor/online/{Guid.NewGuid()}/revoke", null);
        var body = await ReadBodyAsync(response);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(ErrorCodes.OnlineSessionNotFound));
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

    /// <summary>登录并返回完整令牌对（accessToken + refreshToken）。</summary>
    private async Task<(string AccessToken, string RefreshToken)> LoginAsync(string userName, string password)
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
        var data = body.RootElement.GetProperty("data");
        return (data.GetProperty("accessToken").GetString()!, data.GetProperty("refreshToken").GetString()!);
    }

    /// <summary>发送 JSON 请求（POST / PUT / DELETE 等，携带 admin 令牌）。</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? payload)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = payload is null ? null : JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        return await _httpClient.SendAsync(request);
    }

    /// <summary>GET 并解析响应（断言成功状态，携带 admin 令牌）。</summary>
    private static async Task<JsonDocument> GetJsonAsync(string url)
    {
        var response = await SendAsync(HttpMethod.Get, url, null!);
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
