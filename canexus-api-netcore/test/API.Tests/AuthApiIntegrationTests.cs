
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using API;
using Application.Commands;
using Application.Contracts.Commands;
using Application.Contracts.Queries;
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
/// 认证授权集成测试：通过 WebApplicationFactory 走真实 HTTP 管道 + 真实 MySQL（默认库 super_admin_test）。
/// <para>连接串可通过环境变量 SUPER_ADMIN_TEST_CONN 覆盖（沿用 B2 约定）。</para>
/// </summary>
[TestFixture]
public class AuthApiIntegrationTests
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";
    private const string TestUserName = "testuser";
    private const string TestUserPassword = "test@123";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static ICache _cache = null!;
    private static IPasswordHasher _passwordHasher = null!;
    private static string _adminAccessToken = null!;

    /// <summary>测试套件初始化：启动测试宿主并准备种子数据。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = ApiTestSupport.TestConnectionString;
        // API 项目根目录（appsettings.json 所在位置，含 Jwt 节、Serilog 等配置）
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
        _passwordHasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        // 准备无权限测试用户（普通角色，未分配任何菜单）
        await EnsureTestUserAsync();

        // 清理失败计数，保证 admin 可正常登录（历史运行可能残留锁定状态）
        ClearLoginFailState(AdminUserName);
        ClearLoginFailState(TestUserName);

        // admin 登录一次，供后续认证测试复用
        _adminAccessToken = await LoginAndGetAccessTokenAsync(AdminUserName, AdminPassword);
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

    // —— 验证码 ——

    /// <summary>验证验证码接口返回 SVG 格式的 base64 data URI。</summary>
    [Test]
    public async Task Captcha_ReturnsSvgDataUri()
    {
        var response = await _httpClient.GetAsync("/api/auth/captcha");
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(200));
            Assert.That(body.RootElement.GetProperty("data").GetProperty("captchaId").GetString(), Is.Not.Empty);
            var image = body.RootElement.GetProperty("data").GetProperty("imageBase64").GetString();
            Assert.That(image, Does.StartWith("data:image/svg+xml;base64,"), "验证码图片应为 SVG base64 data URI");
        });
    }

    // —— 登录 ——

    /// <summary>验证验证码错误时登录返回 400 及业务错误码 40104。</summary>
    [Test]
    public async Task Login_WrongCaptcha_Returns400WithBusinessCode()
    {
        var captchaId = await GenerateCaptchaAsync();

        var response = await PostJsonAsync("/api/auth/login", new
        {
            userName = AdminUserName,
            password = AdminPassword,
            captchaId,
            captchaCode = "XXXX"
        });
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "验证码错误应返回 HTTP 400");
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40104));
        });
    }

    /// <summary>验证密码错误时登录返回 401 并写入失败登录日志。</summary>
    [Test]
    public async Task Login_WrongPassword_Returns401AndWritesLoginLog()
    {
        var (captchaId, captchaCode) = await GetCaptchaAsync();

        var response = await PostJsonAsync("/api/auth/login", new
        {
            userName = AdminUserName,
            password = "wrong-password",
            captchaId,
            captchaCode
        });
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "密码错误应返回 HTTP 401");
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40103));
        });

        // 登录日志已记录失败（真实库查询）
        var hasFailLog = await _db.Queryable<SysLoginLog>()
            .AnyAsync(l => l.UserName == AdminUserName && l.Status == ExecutionStatus.Failure);
        Assert.That(hasFailLog, Is.True, "失败登录应写入 SysLoginLog");

        ClearLoginFailState(AdminUserName);
    }

    /// <summary>验证密码连续错误五次后账号被锁定。</summary>
    [Test]
    public async Task Login_FiveConsecutiveFailures_LocksAccount()
    {
        // 连续失败 5 次（每次新验证码），第 6 次请求应命中锁定
        for (var i = 0; i < 5; i++)
        {
            var (captchaId, captchaCode) = await GetCaptchaAsync();
            await PostJsonAsync("/api/auth/login", new
            {
                userName = TestUserName,
                password = "bad-password",
                captchaId,
                captchaCode
            });
        }

        var (finalCaptchaId, finalCaptchaCode) = await GetCaptchaAsync();
        var response = await PostJsonAsync("/api/auth/login", new
        {
            userName = TestUserName,
            password = TestUserPassword,
            captchaId = finalCaptchaId,
            captchaCode = finalCaptchaCode
        });
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40105), "连续失败达上限应返回锁定错误码");
            Assert.That(body.RootElement.GetProperty("message").GetString(), Does.Contain("锁定"));
        });

        ClearLoginFailState(TestUserName);
    }

    /// <summary>验证登录成功后返回包含访问令牌与刷新令牌的令牌对。</summary>
    [Test]
    public async Task Login_Success_ReturnsTokenPair()
    {
        var (captchaId, captchaCode) = await GetCaptchaAsync();

        var response = await PostJsonAsync("/api/auth/login", new
        {
            userName = AdminUserName,
            password = AdminPassword,
            captchaId,
            captchaCode
        });
        var body = await ReadBodyAsync(response);
        var data = body.RootElement.GetProperty("data");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(200));
            Assert.That(data.GetProperty("accessToken").GetString(), Is.Not.Empty);
            Assert.That(data.GetProperty("refreshToken").GetString(), Is.Not.Empty);
            Assert.That(data.GetProperty("expiresIn").GetInt64(), Is.GreaterThan(0));
        });
    }

    // —— 刷新令牌轮换 ——

    /// <summary>验证刷新令牌会轮换出新令牌且旧刷新令牌立即失效。</summary>
    [Test]
    public async Task Refresh_RotatesToken_OldRefreshTokenFails()
    {
        // 独立登录拿到一对令牌，避免影响其他测试
        var (_, tokens) = await LoginAsync(AdminUserName, AdminPassword);

        // 第一次刷新：成功并返回新令牌对
        var response = await PostJsonAsync("/api/auth/refresh", new { refreshToken = tokens!.RefreshToken });
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.RootElement.GetProperty("data").GetProperty("refreshToken").GetString(),
                Is.Not.EqualTo(tokens!.RefreshToken), "刷新应轮换出新 refreshToken");
        });

        // 旧 refreshToken 再用：失败（已吊销）
        var reuseResponse = await PostJsonAsync("/api/auth/refresh", new { refreshToken = tokens!.RefreshToken });
        var reuseBody = await ReadBodyAsync(reuseResponse);

        Assert.Multiple(() =>
        {
            Assert.That(reuseResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(reuseBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40106));
        });
    }

    // —— 登出 ——

    /// <summary>验证登出后刷新令牌被吊销且无法再用于刷新。</summary>
    [Test]
    public async Task Logout_RevokesRefreshToken()
    {
        var (_, tokens) = await LoginAsync(AdminUserName, AdminPassword);

        var logoutResponse = await PostJsonAsync("/api/auth/logout", new { refreshToken = tokens!.RefreshToken },
            _adminAccessToken);
        Assert.That(logoutResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // 登出后再刷新：失败
        var refreshResponse = await PostJsonAsync("/api/auth/refresh", new { refreshToken = tokens!.RefreshToken });
        Assert.That(refreshResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // —— 用户信息 ——

    /// <summary>验证未携带令牌访问用户信息接口返回 401 及统一响应结构。</summary>
    [Test]
    public async Task UserInfo_WithoutToken_Returns401WithUnifiedBody()
    {
        var response = await _httpClient.GetAsync("/api/auth/user-info");
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40100),
                "JwtBearer 挑战应返回统一响应结构");
        });
    }

    /// <summary>验证携带过期令牌访问用户信息接口返回 401 及过期错误码。</summary>
    [Test]
    public async Task UserInfo_WithExpiredToken_Returns401WithExpiredCode()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/user-info");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.invalid");

        var response = await _httpClient.SendAsync(request);
        var body = await ReadBodyAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.Not.EqualTo(200));
    }

    /// <summary>验证超管令牌获取用户信息时返回 super_admin 角色与通配权限标识。</summary>
    [Test]
    public async Task UserInfo_WithAdminToken_ReturnsSuperAdminRolesAndWildcardPermissions()
    {
        var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/user-info", _adminAccessToken);
        var body = await ReadBodyAsync(response);
        var data = body.RootElement.GetProperty("data");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(data.GetProperty("userName").GetString(), Is.EqualTo(AdminUserName));
            Assert.That(data.GetProperty("nickName").GetString(), Is.EqualTo("超级管理员"));
            Assert.That(data.GetProperty("roles").EnumerateArray().Select(r => r.GetString()),
                Does.Contain("super_admin"));
            Assert.That(data.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()),
                Does.Contain("*:*:*"), "超管权限应为通配标识");
        });
    }

    // —— 路由树 ——

    /// <summary>验证路由接口返回按权限过滤后的菜单树。</summary>
    [Test]
    public async Task Routes_ReturnsFilteredMenuTree()
    {
        var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/routes", _adminAccessToken);
        var body = await ReadBodyAsync(response);
        var routes = body.RootElement.GetProperty("data");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            // 顶层应包含首页与三大目录
            var paths = routes.EnumerateArray().Select(r => r.GetProperty("path").GetString()).ToList();
            Assert.That(paths, Does.Contain("/dashboard"));
            Assert.That(paths, Does.Contain("/system"));
            Assert.That(paths, Does.Contain("/monitor"));
            Assert.That(paths, Does.Contain("/tool"));
        });

        // 系统管理子树：含用户管理（component 相对 views），不含按钮
        var system = routes.EnumerateArray()
            .First(r => r.GetProperty("path").GetString() == "/system");
        var childPaths = system.GetProperty("children").EnumerateArray()
            .Select(r => r.GetProperty("path").GetString()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(childPaths, Does.Contain("user"));
            Assert.That(childPaths, Does.Contain("tenant"), "租户管理应挂在系统管理目录下");
        });

        var userRoute = system.GetProperty("children").EnumerateArray()
            .First(r => r.GetProperty("path").GetString() == "user");
        Assert.Multiple(() =>
        {
            Assert.That(userRoute.GetProperty("name").GetString(), Is.EqualTo("SystemUser"), "路由 name 应为完整路径驼峰");
            Assert.That(userRoute.GetProperty("component").GetString(), Is.EqualTo("system/user/index"));
            Assert.That(userRoute.GetProperty("meta").GetProperty("title").GetString(), Is.EqualTo("用户管理"));
            // 按钮不应出现在路由树中
            Assert.That(userRoute.GetProperty("children").GetArrayLength(), Is.EqualTo(0), "按钮不应进入路由树");
        });
    }

    // —— RequirePermission 端点 ——

    /// <summary>验证未携带令牌访问受保护端点返回 401。</summary>
    [Test]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var response = await _httpClient.GetAsync("/api/test-permissions/protected");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>验证超管访问受保护端点返回 200。</summary>
    [Test]
    public async Task ProtectedEndpoint_WithSuperAdmin_Returns200()
    {
        var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/test-permissions/protected", _adminAccessToken);
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "超管应放行全部权限");
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(200));
        });
    }

    /// <summary>验证无权限用户访问受保护端点返回 403。</summary>
    [Test]
    public async Task ProtectedEndpoint_WithNoPermissionUser_Returns403()
    {
        // 普通用户（test_user 角色，无任何菜单权限）登录
        var token = await LoginAndGetAccessTokenAsync(TestUserName, TestUserPassword);

        var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/test-permissions/protected", token);
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "无权限应返回 HTTP 403");
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40300));
        });
    }

    // —— 刷新令牌存储安全（P0-S2）——

    /// <summary>核心回归：刷新令牌必须以 SHA-256 哈希存储，明文串不得命中任何记录（修复前明文直存）。</summary>
    [Test]
    public async Task RefreshToken_StoredAsSha256Hash_NotPlaintext()
    {
        var (_, tokens) = await LoginAsync(AdminUserName, AdminPassword);
        Assert.That(tokens, Is.Not.Null);

        var raw = tokens!.RefreshToken;
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

        var plaintextHit = await _db.Queryable<SysRefreshToken>().AnyAsync(t => t.Token == raw);
        Assert.That(plaintextHit, Is.False, "刷新令牌不得以明文形式存储（应按 SHA-256 哈希存储）");

        var hashHit = await _db.Queryable<SysRefreshToken>().AnyAsync(t => t.Token == expectedHash);
        Assert.That(hashHit, Is.True, "应以刷新令牌的 SHA-256 哈希值存储记录");
    }

    // —— 令牌撤销（P1-A）——

    /// <summary>
    /// 单设备登出：该会话 accessToken 下一次请求即 401（错误码 40107），同用户其他设备会话不受影响。
    /// </summary>
    [Test]
    public async Task Logout_InvalidatesOnlyCurrentSessionAccessToken()
    {
        var (_, sessionA) = await LoginAsync(TestUserName, TestUserPassword);
        var (_, sessionB) = await LoginAsync(TestUserName, TestUserPassword);
        Assert.That(sessionA, Is.Not.Null);
        Assert.That(sessionB, Is.Not.Null);

        var logout = await PostJsonAsync("/api/auth/logout", new { refreshToken = sessionA!.RefreshToken }, sessionA.AccessToken);
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.OK), "登出应成功");

        var revoked = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/user-info", sessionA.AccessToken);
        var revokedBody = await ReadBodyAsync(revoked);
        var otherSession = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/user-info", sessionB!.AccessToken);

        Assert.Multiple(() =>
        {
            Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "登出会话的 accessToken 应立即失效");
            Assert.That(revokedBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40107), "应返回令牌已撤销错误码");
            Assert.That(otherSession.StatusCode, Is.EqualTo(HttpStatusCode.OK), "同用户其他设备会话不应被登出影响");
        });
    }

    /// <summary>管理员按会话强制下线：目标会话 accessToken 下一次请求即 401，且不影响操作者自身会话。</summary>
    [Test]
    public async Task ForceLogout_InvalidatesTargetSessionAccessToken()
    {
        var (_, victim) = await LoginAsync(TestUserName, TestUserPassword);
        Assert.That(victim, Is.Not.Null);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(victim!.RefreshToken))).ToLowerInvariant();
        var session = await _db.Queryable<SysRefreshToken>().Where(it => it.Token == hash).FirstAsync();
        Assert.That(session, Is.Not.Null, "应能按刷新令牌哈希反查到在线会话记录");

        var revoke = await SendAuthorizedAsync(HttpMethod.Post, $"/api/monitor/online/{session!.Id}/revoke", _adminAccessToken);
        Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.OK), "强制下线应成功");

        var revoked = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/user-info", victim.AccessToken);
        var revokedBody = await ReadBodyAsync(revoked);
        var adminStillValid = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/user-info", _adminAccessToken);

        Assert.Multiple(() =>
        {
            Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "被强制下线的会话 accessToken 应立即失效");
            Assert.That(revokedBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40107));
            Assert.That(adminStillValid.StatusCode, Is.EqualTo(HttpStatusCode.OK), "强制下线不应影响其他用户的会话");
        });
    }

    /// <summary>自助改密：旧 accessToken 立即 401，之后旧密码不可登录、新密码可登录并正常访问。</summary>
    [Test]
    public async Task ChangePassword_InvalidatesOldAccessToken_NewLoginWorks()
    {
        const string newPassword = "test@4567";
        var (_, tokens) = await LoginAsync(TestUserName, TestUserPassword);
        Assert.That(tokens, Is.Not.Null);

        try
        {
            var change = await PostJsonAsync(
                "/api/auth/change-password",
                new { oldPassword = TestUserPassword, newPassword },
                tokens!.AccessToken);
            Assert.That(change.StatusCode, Is.EqualTo(HttpStatusCode.OK), "改密应成功");

            var revoked = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/user-info", tokens.AccessToken);
            var revokedBody = await ReadBodyAsync(revoked);
            Assert.Multiple(() =>
            {
                Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "改密后旧 accessToken 应立即失效");
                Assert.That(revokedBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40107));
            });

            var (newLogin, newTokens) = await LoginAsync(TestUserName, newPassword);
            Assert.That(newLogin.StatusCode, Is.EqualTo(HttpStatusCode.OK), "新密码应可登录");
            Assert.That(newTokens, Is.Not.Null);

            var info = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/user-info", newTokens!.AccessToken);
            Assert.That(info.StatusCode, Is.EqualTo(HttpStatusCode.OK), "新登录签发的 accessToken 应可正常访问");
        }
        finally
        {
            await EnsureTestUserAsync();
            ClearLoginFailState(TestUserName);
        }
    }

    /// <summary>停用用户：此前签发的 accessToken 下一次请求即 401（无需等待令牌自然过期）。</summary>
    [Test]
    public async Task DisableUser_InvalidatesExistingAccessToken()
    {
        var (_, tokens) = await LoginAsync(TestUserName, TestUserPassword);
        Assert.That(tokens, Is.Not.Null);

        var user = await _db.Queryable<SysUser>().Where(it => it.UserName == TestUserName).FirstAsync();
        Assert.That(user, Is.Not.Null);

        try
        {
            var disable = await SendJsonAuthorizedAsync(
                HttpMethod.Put,
                $"/api/system/user/{user!.Id}/{user.Version}/status",
                new { status = 0 },
                _adminAccessToken);
            Assert.That(disable.StatusCode, Is.EqualTo(HttpStatusCode.OK), "停用用户应成功");

            var revoked = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/user-info", tokens!.AccessToken);
            var revokedBody = await ReadBodyAsync(revoked);
            Assert.Multiple(() =>
            {
                Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "用户停用后 accessToken 应立即失效");
                Assert.That(revokedBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40107));
            });
        }
        finally
        {
            await _db.Updateable<SysUser>()
                .SetColumns(it => it.Status == CommonStatus.Enabled)
                .Where(it => it.Id == user!.Id)
                .ExecuteCommandAsync();

            // 测试直接写库恢复状态不会触发领域事件：显式递增令牌版本并清理缓存，
            // 否则后续用例会读到缓存的「已停用」状态而被误判为令牌已撤销（夹具共享同一测试用户）
            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ITokenVersionService>().RevokeAllAsync(user!.Id);
            ClearLoginFailState(TestUserName);
        }
    }

    // —— 测试辅助 ——

    /// <summary>准备无权限测试用户：test_user 角色（未分配菜单）+ testuser 用户，幂等。</summary>
    private async Task EnsureTestUserAsync()
    {
        var role = (await _db.Queryable<SysRole>().Where(r => r.RoleKey == "test_user").ToListAsync()).FirstOrDefault();
        if (role is null)
        {
            role = new SysRole { RoleName = "测试角色", RoleKey = "test_user", Status = CommonStatus.Enabled };
            await _db.Insertable(role).ExecuteCommandAsync();
        }

        var user = (await _db.Queryable<SysUser>().Where(u => u.UserName == TestUserName).ToListAsync()).FirstOrDefault();
        if (user is null)
        {
            await _db.Insertable(new SysUser
            {
                UserName = TestUserName,
                NickName = "无权限测试用户",
                PasswordHash = _passwordHasher.Hash(TestUserPassword),
                Status = CommonStatus.Enabled
            }).ExecuteCommandAsync();
        }
        else
        {
            // 保证密码与状态符合预期（历史运行可能改动）
            user.PasswordHash = _passwordHasher.Hash(TestUserPassword);
            user.Status = CommonStatus.Enabled;
            await _db.Updateable(user).ExecuteCommandAsync();
        }
    }

    /// <summary>调用验证码接口并从 IMemoryCache 读取明文验证码（走真实 HTTP 生成链路）。</summary>
    private async Task<(string CaptchaId, string CaptchaCode)> GetCaptchaAsync()
    {
        var captchaId = await GenerateCaptchaAsync();
        var code = await _cache.GetAsync<string>(ICaptchaService.CacheKeyPrefix + captchaId);
        Assert.That(code, Is.Not.Null, "验证码应已写入缓存");
        return (captchaId, code!);
    }

    /// <summary>调用验证码接口仅取 captchaId。</summary>
    private async Task<string> GenerateCaptchaAsync()
    {
        var response = await _httpClient.GetAsync("/api/auth/captcha");
        response.EnsureSuccessStatusCode();
        var body = await ReadBodyAsync(response);
        return body.RootElement.GetProperty("data").GetProperty("captchaId").GetString()!;
    }

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

    /// <summary>带 token 的 GET 请求。</summary>
    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _httpClient.SendAsync(request);
    }

    /// <summary>带 token 且携带 JSON 请求体的请求（PUT / POST）。</summary>
    private async Task<HttpResponseMessage> SendJsonAuthorizedAsync(HttpMethod method, string url, object payload, string accessToken)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _httpClient.SendAsync(request);
    }

    /// <summary>清除指定用户名的登录失败计数（避免多次运行测试累计锁定）。</summary>
    private void ClearLoginFailState(string userName)
    {
        _cache.RemoveAsync(AuthLoginCommandHandler.LoginFailCacheKeyPrefix + userName).GetAwaiter().GetResult();
    }

    /// <summary>读取响应体为 JsonDocument。</summary>
    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
