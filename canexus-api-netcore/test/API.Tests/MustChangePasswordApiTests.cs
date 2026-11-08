using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using Application.Commands;
using Domain.Entities;
using Domain.Services.Auth;
using Domain.Services.Security;
using Domain.Shared.Enums;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace API.Tests;
/// <summary>
/// 初始密码策略（P0-S4）集成测试：
/// 走真实 HTTP 管道 + 真实 MySQL（super_admin_test），
/// 重点验证：创建用户使用默认密码时登录响应携带 mustChangePassword=true；
/// 待改密用户在强制改密完成前访问受保护端点被 40302 拦截；
/// 自助改密（原密码校验 + 清除标记）后拦截解除；
/// 管理员重置密码后用户再次登录须强制改密。
/// </summary>
[TestFixture]
public class MustChangePasswordApiTests
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";

    /// <summary>与修复后的 UserConstants.DefaultPassword 一致的默认初始密码（满足 6-20 位含字母数字）。</summary>
    private const string DefaultPassword = "Aa123456";

    private const string NewPassword = "New@123456";
    private const string ResetPassword = "Reset@123456";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static ICache _cache = null!;
    private static string _adminToken = null!;

    /// <summary>测试套件初始化：启动测试宿主并准备种子数据。</summary>
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
        _cache = _factory.Services.GetRequiredService<ICache>();

        ClearLoginFailState(AdminUserName);
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

    // —— 默认密码与首登强制改密 ——

    /// <summary>核心回归：以默认密码创建用户后，登录响应必须携带 mustChangePassword=true（修复前无此字段）。</summary>
    [Test]
    public async Task CreateUserWithDefaultPassword_Login_ReturnsMustChangeFlag()
    {
        var userName = await CreateUserViaApiAsync("mcp-flag");

        var (response, _) = await LoginAsync(userName, DefaultPassword);
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.RootElement.GetProperty("data").GetProperty("mustChangePassword").GetBoolean(),
                Is.True, "使用默认初始密码的用户首登应返回 mustChangePassword=true");
        });
    }

    /// <summary>核心回归：待改密用户访问受保护端点（即使有超管角色）必须被 40302 拦截。</summary>
    [Test]
    public async Task MustChangeUser_ProtectedEndpoint_RejectedWith40302()
    {
        var userName = await CreateUserViaApiAsync("mcp-block");

        // 管理员把该用户提升为超管角色：证明拦截发生在权限校验之前
        var userId = await ApiTestSupport.GetEntityIdAsync<SysUser>(_db, u => u.UserName == userName);
        var role = await _db.Queryable<SysRole>().Where(r => r.RoleKey == "super_admin").FirstAsync();
        await _db.Insertable(new SysUserRole { UserId = userId, RoleId = role!.Id }).ExecuteCommandAsync();

        var token = await LoginAndGetAccessTokenAsync(userName, DefaultPassword);
        var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/test-permissions/protected", token);
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40302),
                "待改密用户应返回强制改密错误码，而非权限校验结果");
        });
    }

    /// <summary>验证待改密用户仍可访问白名单端点（user-info / routes / logout 供前端完成改密流程）。</summary>
    [Test]
    public async Task MustChangeUser_WhitelistedEndpoints_Accessible()
    {
        var userName = await CreateUserViaApiAsync("mcp-allow");
        var token = await LoginAndGetAccessTokenAsync(userName, DefaultPassword);

        var userInfo = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/user-info", token);
        var routes = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/routes", token);

        Assert.Multiple(() =>
        {
            Assert.That(userInfo.StatusCode, Is.EqualTo(HttpStatusCode.OK), "待改密用户应能访问 user-info");
            Assert.That(routes.StatusCode, Is.EqualTo(HttpStatusCode.OK), "待改密用户应能访问 routes");
        });
    }

    /// <summary>验证自助改密：原密码错误返回 400208；成功后旧 accessToken 立即失效（40107），
    /// 新密码重新登录标记清除且新令牌访问受保护端点不再被强制改密拦截。</summary>
    [Test]
    public async Task ChangePassword_WrongOld_ThenSuccess_ClearsFlagAndRestoresAccess()
    {
        var userName = await CreateUserViaApiAsync("mcp-self");
        var token = await LoginAndGetAccessTokenAsync(userName, DefaultPassword);

        // 原密码错误 → 400208
        var wrongOld = await PostJsonAsync("/api/auth/change-password", new { oldPassword = "wrong-old", newPassword = NewPassword }, token);
        var wrongOldBody = await ReadBodyAsync(wrongOld);
        Assert.Multiple(() =>
        {
            Assert.That(wrongOld.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(wrongOldBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400208), "原密码错误应返回 400208");
        });

        // 改密成功 → 200
        var change = await PostJsonAsync("/api/auth/change-password", new { oldPassword = DefaultPassword, newPassword = NewPassword }, token);
        Assert.That(change.StatusCode, Is.EqualTo(HttpStatusCode.OK), "自助改密应成功");

        // P1-A：改密后此前签发的 accessToken 立即失效（401 + 40107），须重新登录
        var revoked = await SendAuthorizedAsync(HttpMethod.Get, "/api/test-permissions/protected", token);
        var revokedBody = await ReadBodyAsync(revoked);
        Assert.Multiple(() =>
        {
            Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "改密后旧 accessToken 应立即失效");
            Assert.That(revokedBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40107), "应返回令牌已撤销错误码");
        });

        // 新密码重新登录：mustChangePassword=false，且新 accessToken 访问受保护端点不再被强制改密拦截（无权限回落为 40300）
        var (relogin, newTokens) = await LoginAsync(userName, NewPassword);
        var reloginBody = await ReadBodyAsync(relogin);
        Assert.Multiple(() =>
        {
            Assert.That(relogin.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(reloginBody.RootElement.GetProperty("data").GetProperty("mustChangePassword").GetBoolean(),
                Is.False, "改密后再次登录 mustChangePassword 应为 false");
        });

        var after = await SendAuthorizedAsync(HttpMethod.Get, "/api/test-permissions/protected", newTokens!.AccessToken);
        var afterBody = await ReadBodyAsync(after);
        Assert.Multiple(() =>
        {
            Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(afterBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40300),
                "改密后新登录不应再被 40302 拦截，应正常进入权限校验");
        });
    }

    /// <summary>验证管理员重置密码后，用户再次登录必须强制改密。</summary>
    [Test]
    public async Task AdminResetPassword_SetsMustChangeFlag()
    {
        var userName = await CreateUserViaApiAsync("mcp-reset");
        var userId = await ApiTestSupport.GetEntityIdAsync<SysUser>(_db, u => u.UserName == userName);
        var version = await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId);

        var reset = await SendForJsonAsync(HttpMethod.Put, $"/api/system/user/{userId}/{version}/password",
            new { password = ResetPassword }, _adminToken);
        Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.OK), "管理员重置密码应成功");

        var (login, _) = await LoginAsync(userName, ResetPassword);
        var body = await ReadBodyAsync(login);

        Assert.Multiple(() =>
        {
            Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.RootElement.GetProperty("data").GetProperty("mustChangePassword").GetBoolean(),
                Is.True, "管理员重置密码后用户再次登录应被强制改密");
        });
    }

    // —— 测试数据辅助 ——

    /// <summary>经真实 API 创建用户（不指定密码 → 使用默认初始密码），返回用户名。</summary>
    private async Task<string> CreateUserViaApiAsync(string prefix)
    {
        var userName = $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(24, prefix.Length + 9)];
        var response = await PostJsonAsync("/api/system/user", new
        {
            userName,
            nickName = $"强制改密测试-{prefix}",
            status = 1,
            roleIds = Array.Empty<Guid>(),
            password = (string?)null
        }, _adminToken);
        var body = await ReadBodyAsync(response);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"创建用户应成功：{body.RootElement.GetRawText()}");
        return userName;
    }

    // —— HTTP 辅助 ——

    /// <summary>登录并返回完整令牌对。</summary>
    private async Task<(HttpResponseMessage Response, Application.Contracts.Auth.TokenPairDto? Tokens)> LoginAsync(string userName, string password)
    {
        var (captchaId, captchaCode) = await GetCaptchaAsync();
        var response = await PostJsonAsync("/api/auth/login", new
        {
            userName,
            password,
            captchaId,
            captchaCode
        });

        if (!response.IsSuccessStatusCode)
        {
            return (response, null);
        }

        var body = await ReadBodyAsync(response);
        var data = body.RootElement.GetProperty("data");
        return (response, new Application.Contracts.Auth.TokenPairDto
        {
            AccessToken = data.GetProperty("accessToken").GetString()!,
            RefreshToken = data.GetProperty("refreshToken").GetString()!,
            ExpiresIn = data.GetProperty("expiresIn").GetInt64()
        });
    }

    /// <summary>登录并返回 accessToken。</summary>
    private async Task<string> LoginAndGetAccessTokenAsync(string userName, string password)
    {
        var (response, tokens) = await LoginAsync(userName, password);
        if (!response.IsSuccessStatusCode || tokens is null)
        {
            var content = await response.Content.ReadAsStringAsync();
            Assert.Fail($"登录应成功：{userName}，HTTP {(int)response.StatusCode}，响应：{content}");
        }

        return tokens!.AccessToken;
    }

    /// <summary>POST JSON 请求。</summary>
    private async Task<HttpResponseMessage> PostJsonAsync(string url, object payload, string? accessToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await _httpClient.SendAsync(request);
    }

    /// <summary>带令牌的 PUT 请求。</summary>
    private async Task<HttpResponseMessage> SendForJsonAsync(HttpMethod method, string url, object payload, string accessToken)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _httpClient.SendAsync(request);
    }

    /// <summary>带令牌的 GET 请求。</summary>
    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _httpClient.SendAsync(request);
    }

    /// <summary>获取图形验证码（经缓存读取真实验证码内容）。</summary>
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

    /// <summary>清除指定用户名的登录失败计数。</summary>
    private void ClearLoginFailState(string userName)
    {
        _cache.RemoveAsync(AuthLoginCommandHandler.LoginFailCacheKeyPrefix + userName).GetAwaiter().GetResult();
    }

    /// <summary>读取响应正文为 JSON 文档。</summary>
    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
