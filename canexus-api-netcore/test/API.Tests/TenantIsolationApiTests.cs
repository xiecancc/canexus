using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using API.Middlewares;
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
/// 多租户越权隔离（P0-S1）集成测试——多租户开启（MultiTenancy:Enabled=true）：
/// 走真实 HTTP 管道 + 真实 MySQL（super_admin_test），
/// 重点验证：登录用户携带与自身租户不一致的请求头应被 403 拒绝；
/// 平台用户（admin）请求不存在 / 停用 / 到期租户应被 400 拒绝；
/// 租户用户不带头时按 JWT 声明租户收敛数据范围。
/// </summary>
[TestFixture]
public class TenantIsolationApiTests
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";
    private const string TenantUserPassword = "test@123";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static ICache _cache = null!;
    private static IPasswordHasher _passwordHasher = null!;
    private static string _adminToken = null!;

    /// <summary>测试套件初始化：启动测试宿主（多租户开启）并以平台 admin 登录。</summary>
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
                        ["MultiTenancy:Enabled"] = "true" // 本套件专门验证多租户开启场景
                    });
                });
            });

        _httpClient = _factory.CreateClient();
        _db = _factory.Services.GetRequiredService<ISqlSugarClient>();
        _cache = _factory.Services.GetRequiredService<ICache>();
        _passwordHasher = _factory.Services.GetRequiredService<IPasswordHasher>();

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

    // —— 平台用户（无租户归属）——

    /// <summary>验证平台 admin 不携带租户头时正常访问（平台数据范围）。</summary>
    [Test]
    public async Task PlatformAdmin_NoTenantHeader_Succeeds()
    {
        var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/user-info", _adminToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>验证平台 admin 携带不存在租户头时返回 400801（租户不存在）。</summary>
    [Test]
    public async Task PlatformAdmin_NonexistentTenantHeader_ReturnsTenantNotFound()
    {
        var response = await SendAuthorizedWithTenantAsync(HttpMethod.Get, "/api/auth/user-info", _adminToken, Guid.NewGuid());
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400801), "不存在的租户应返回租户不存在错误码");
        });
    }

    /// <summary>验证停用租户的请求头被拒绝（400805）。</summary>
    [Test]
    public async Task DisabledTenant_Header_Rejected()
    {
        var tenantId = await CreateTenantAsync("iso-dis", enabled: false, expireYears: 10);
        var response = await SendAuthorizedWithTenantAsync(HttpMethod.Get, "/api/auth/user-info", _adminToken, tenantId);
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400805), "停用租户应返回租户已停用错误码");
        });
    }

    /// <summary>验证到期租户的请求头被拒绝（400806）。</summary>
    [Test]
    public async Task ExpiredTenant_Header_Rejected()
    {
        var tenantId = await CreateTenantAsync("iso-exp", enabled: true, expireYears: -1);
        var response = await SendAuthorizedWithTenantAsync(HttpMethod.Get, "/api/auth/user-info", _adminToken, tenantId);
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400806), "到期租户应返回租户已到期错误码");
        });
    }

    // —— 租户用户（JWT 声明租户）——

    /// <summary>验证租户用户携带自身租户头时正常访问。</summary>
    [Test]
    public async Task TenantUser_OwnTenantHeader_Succeeds()
    {
        var tenantId = await CreateTenantAsync("iso-own", enabled: true, expireYears: 10);
        var userName = await CreateTenantUserAsync("iso-own-user", tenantId, superAdmin: false);
        var token = await LoginAndGetAccessTokenAsync(userName, TenantUserPassword, tenantId);

        var response = await SendAuthorizedWithTenantAsync(HttpMethod.Get, "/api/auth/user-info", token, tenantId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>验证租户用户不携带租户头时，按 JWT 声明租户收敛数据范围（正常访问自身数据）。</summary>
    [Test]
    public async Task TenantUser_NoTenantHeader_ScopedToClaimTenant()
    {
        var tenantId = await CreateTenantAsync("iso-noh", enabled: true, expireYears: 10);
        var userName = await CreateTenantUserAsync("iso-noh-user", tenantId, superAdmin: false);
        var token = await LoginAndGetAccessTokenAsync(userName, TenantUserPassword, tenantId);

        var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/user-info", token);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>验证租户用户访问受保护端点（自身租户）正常放行。</summary>
    [Test]
    public async Task TenantUser_OwnTenant_ProtectedEndpoint_Ok()
    {
        var tenantId = await CreateTenantAsync("iso-pok", enabled: true, expireYears: 10);
        var userName = await CreateTenantUserAsync("iso-pok-user", tenantId, superAdmin: true);
        var token = await LoginAndGetAccessTokenAsync(userName, TenantUserPassword, tenantId);

        var response = await SendAuthorizedWithTenantAsync(HttpMethod.Get, "/api/test-permissions/protected", token, tenantId);
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(200));
        });
    }

    /// <summary>核心回归：租户用户把请求头换成其他租户时，即使拥有超管角色也必须被 403 拒绝（跨租户越权）。</summary>
    [Test]
    public async Task TenantUser_CrossTenantHeader_RejectedForbidden()
    {
        var tenant1 = await CreateTenantAsync("iso-x1", enabled: true, expireYears: 10);
        var tenant2 = await CreateTenantAsync("iso-x2", enabled: true, expireYears: 10);
        var userName = await CreateTenantUserAsync("iso-x-user", tenant1, superAdmin: true);
        var token = await LoginAndGetAccessTokenAsync(userName, TenantUserPassword, tenant1);

        var response = await SendAuthorizedWithTenantAsync(HttpMethod.Get, "/api/test-permissions/protected", token, tenant2);
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "跨租户请求头应返回 403");
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40301), "应返回跨租户访问拒绝错误码");
        });
    }

    // —— S1：已认证会话的租户状态全链路复查（accessToken 签发后租户被停用）——

    /// <summary>验证租户用户登录后租户被停用：旧 accessToken 无论是否携带租户头均返回 403（400805）。</summary>
    [Test]
    public async Task TenantUser_DisabledAfterLogin_OldAccessTokenForbidden()
    {
        var tenantId = await CreateTenantAsync("s1-live", enabled: true, expireYears: 10);
        var userName = await CreateTenantUserAsync("s1-live-user", tenantId, superAdmin: false);
        try
        {
            var token = await LoginAndGetAccessTokenAsync(userName, TenantUserPassword, tenantId);

            // 经租户更新接口停用（走领域事件 → 租户访问缓存主动失效，覆盖"状态变更即时生效"链路）
            await DisableTenantViaApiAsync(tenantId);

            var noHeader = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/user-info", token);
            var noHeaderBody = await ReadBodyAsync(noHeader);
            var withHeader = await SendAuthorizedWithTenantAsync(HttpMethod.Get, "/api/auth/user-info", token, tenantId);
            var withHeaderBody = await ReadBodyAsync(withHeader);

            Assert.Multiple(() =>
            {
                Assert.That(noHeader.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "无 X-Tenant-Id 头的租户用户同样应复查租户状态");
                Assert.That(noHeaderBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400805), "应返回租户已停用错误码");
                Assert.That(withHeader.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "携带自身租户头的旧令牌同样应被拒绝");
                Assert.That(withHeaderBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400805), "应返回租户已停用错误码");
            });
        }
        finally
        {
            await DeleteTenantUserAsync(userName, tenantId);
        }
    }

    /// <summary>验证租户被停用后刷新令牌被拒绝（403 40301），不签发新 accessToken。</summary>
    [Test]
    public async Task TenantUser_DisabledTenant_RefreshRejected()
    {
        var tenantId = await CreateTenantAsync("s1-rf", enabled: true, expireYears: 10);
        var userName = await CreateTenantUserAsync("s1-rf-user", tenantId, superAdmin: false);
        try
        {
            var (_, refreshToken) = await LoginAndGetTokenPairAsync(userName, TenantUserPassword, tenantId);
            await DisableTenantViaApiAsync(tenantId);

            // 不携带租户头，绕过中间件对匿名请求的租户头校验，直达刷新处理器验证其租户状态校验
            var response = await _httpClient.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
            var body = await ReadBodyAsync(response);

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "停用租户的刷新令牌应被拒绝");
                Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40301), "应返回租户访问拒绝错误码");
            });
        }
        finally
        {
            await DeleteTenantUserAsync(userName, tenantId);
        }
    }

    // —— P1-A1：登录链路租户状态校验（停用 / 到期 / 不存在的租户，其用户不可登录并记录登录日志）——

    /// <summary>验证停用租户的用户无法登录（400805）且登录日志记录原因。</summary>
    [Test]
    public async Task TenantUser_DisabledTenant_LoginRejected()
    {
        var tenantId = await CreateTenantAsync("a1-dis", enabled: false, expireYears: 10);
        var userName = await CreateTenantUserAsync("a1-dis-user", tenantId, superAdmin: false);
        try
        {
            var response = await LoginAsync(userName, TenantUserPassword);
            var body = await ReadBodyAsync(response);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "停用租户的用户应无法登录");
                Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400805), "应返回租户已停用错误码");
            });

            var log = await _db.Queryable<SysLoginLog>()
                .Where(l => l.UserName == userName)
                .OrderBy(l => l.LoginTime, OrderByType.Desc)
                .FirstAsync();
            Assert.That(log, Is.Not.Null, "登录失败应写入登录日志");
            Assert.That(log!.Msg, Does.Contain("租户已停用"), "登录日志应记录租户停用原因");
        }
        finally
        {
            _db.Deleteable<SysLoginLog>().Where(l => l.UserName == userName).ExecuteCommand();
            _db.Deleteable<SysUser>().Where(u => u.UserName == userName).ExecuteCommand();
            _db.Deleteable<SysTenant>().Where(t => t.Id == tenantId).ExecuteCommand();
        }
    }

    /// <summary>验证到期租户的用户无法登录（400806）且登录日志记录原因。</summary>
    [Test]
    public async Task TenantUser_ExpiredTenant_LoginRejected()
    {
        var tenantId = await CreateTenantAsync("a1-exp", enabled: true, expireYears: -1);
        var userName = await CreateTenantUserAsync("a1-exp-user", tenantId, superAdmin: false);
        try
        {
            var response = await LoginAsync(userName, TenantUserPassword);
            var body = await ReadBodyAsync(response);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "到期租户的用户应无法登录");
                Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400806), "应返回租户已到期错误码");
            });

            var log = await _db.Queryable<SysLoginLog>()
                .Where(l => l.UserName == userName)
                .OrderBy(l => l.LoginTime, OrderByType.Desc)
                .FirstAsync();
            Assert.That(log, Is.Not.Null, "登录失败应写入登录日志");
            Assert.That(log!.Msg, Does.Contain("租户已到期"), "登录日志应记录租户到期原因");
        }
        finally
        {
            _db.Deleteable<SysLoginLog>().Where(l => l.UserName == userName).ExecuteCommand();
            _db.Deleteable<SysUser>().Where(u => u.UserName == userName).ExecuteCommand();
            _db.Deleteable<SysTenant>().Where(t => t.Id == tenantId).ExecuteCommand();
        }
    }

    /// <summary>验证归属不存在租户的用户无法登录（400801）。</summary>
    [Test]
    public async Task TenantUser_NonexistentTenant_LoginRejected()
    {
        var userName = await CreateTenantUserAsync("a1-nf-user", Guid.NewGuid(), superAdmin: false);
        try
        {
            var response = await LoginAsync(userName, TenantUserPassword);
            var body = await ReadBodyAsync(response);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "归属不存在租户的用户应无法登录");
                Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400801), "应返回租户不存在错误码");
            });
        }
        finally
        {
            _db.Deleteable<SysUser>().Where(u => u.UserName == userName).ExecuteCommand();
        }
    }

    // —— 测试数据辅助 ——

    /// <summary>直插一个租户（唯一 key 带随机后缀），返回租户 Id。</summary>
    private async Task<Guid> CreateTenantAsync(string prefix, bool enabled, int expireYears)
    {
        var tenant = new SysTenant
        {
            TenantKey = $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(24, prefix.Length + 9)],
            TenantName = $"隔离测试租户-{prefix}",
            Status = enabled ? CommonStatus.Enabled : CommonStatus.Disabled,
            ExpireTime = DateTime.UtcNow.AddYears(expireYears)
        };
        await _db.Insertable(tenant).ExecuteCommandAsync();
        return tenant.Id;
    }

    /// <summary>直插一个租户用户（可选：构建租户级角色+按钮菜单权限链，使受保护端点可访问）。返回用户名。</summary>
    private async Task<string> CreateTenantUserAsync(string prefix, Guid tenantId, bool superAdmin)
    {
        var userName = $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(24, prefix.Length + 9)];
        var user = new SysUser
        {
            UserName = userName,
            NickName = "租户隔离测试用户",
            PasswordHash = _passwordHasher.Hash(TenantUserPassword),
            TenantId = tenantId,
            Status = CommonStatus.Enabled
        };
        await _db.Insertable(user).ExecuteCommandAsync();

        if (superAdmin)
        {
            // 租户级权限链：角色 + 按钮菜单（test:protected:list）+ 关联行——租户过滤器对全部实体生效，
            // 平台级超管角色对租户用户不可见，故须构造同租户的角色/菜单/关联
            var role = new SysRole
            {
                RoleKey = $"tenant_iso_admin_{Guid.NewGuid():N}"[..Math.Min(36, 16 + 9)],
                RoleName = "隔离测试租户管理员",
                Status = CommonStatus.Enabled,
                TenantId = tenantId
            };
            await _db.Insertable(role).ExecuteCommandAsync();

            var menu = new SysMenu
            {
                MenuName = "隔离测试权限点",
                Path = "/iso-test",
                MenuType = MenuType.Button,
                Perms = "test:protected:list",
                Status = CommonStatus.Enabled,
                TenantId = tenantId
            };
            await _db.Insertable(menu).ExecuteCommandAsync();

            await _db.Insertable(new SysRoleMenu { RoleId = role.Id, MenuId = menu.Id, TenantId = tenantId }).ExecuteCommandAsync();
            await _db.Insertable(new SysUserRole { UserId = user.Id, RoleId = role.Id, TenantId = tenantId }).ExecuteCommandAsync();
        }

        return userName;
    }

    /// <summary>经租户更新接口（平台 admin）把租户置为停用，用于验证状态变更后的缓存失效与全链路拦截。</summary>
    private async Task DisableTenantViaApiAsync(Guid tenantId)
    {
        var tenant = await _db.Queryable<SysTenant>().Where(t => t.Id == tenantId).FirstAsync();
        Assert.That(tenant, Is.Not.Null, "待停用租户应存在");

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/system/tenant/{tenantId}/{tenant!.Version}")
        {
            Content = JsonContent.Create(new
            {
                tenantKey = tenant.TenantKey,
                tenantName = tenant.TenantName,
                status = CommonStatus.Disabled,
                contactPhone = tenant.ContactPhone,
                expireTime = tenant.ExpireTime
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        var response = await _httpClient.SendAsync(request);
        var body = await ReadBodyAsync(response);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"停用租户应成功：{body.RootElement.GetRawText()}");
    }

    /// <summary>清理测试租户用户及其令牌 / 登录日志与租户数据。</summary>
    private async Task DeleteTenantUserAsync(string userName, Guid tenantId)
    {
        var user = await _db.Queryable<SysUser>().Where(u => u.UserName == userName).FirstAsync();
        if (user is not null)
        {
            _db.Deleteable<SysRefreshToken>().Where(r => r.UserId == user.Id).ExecuteCommand();
        }

        _db.Deleteable<SysLoginLog>().Where(l => l.UserName == userName).ExecuteCommand();
        _db.Deleteable<SysUser>().Where(u => u.UserName == userName).ExecuteCommand();
        _db.Deleteable<SysTenant>().Where(t => t.Id == tenantId).ExecuteCommand();
    }

    // —— HTTP 辅助 ——

    /// <summary>登录并返回 accessToken 与 refreshToken（可携带租户头）。</summary>
    private async Task<(string AccessToken, string RefreshToken)> LoginAndGetTokenPairAsync(string userName, string password, Guid? tenantId = null)
    {
        var (captchaId, captchaCode) = await GetCaptchaAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                userName,
                password,
                captchaId,
                captchaCode
            })
        };
        if (tenantId.HasValue)
        {
            request.Headers.Add(TenantResolutionMiddleware.TenantHeader, tenantId.Value.ToString());
        }

        var response = await _httpClient.SendAsync(request);
        var body = await ReadBodyAsync(response);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"登录应成功：{body.RootElement.GetRawText()}");
        var data = body.RootElement.GetProperty("data");
        return (data.GetProperty("accessToken").GetString()!, data.GetProperty("refreshToken").GetString()!);
    }

    /// <summary>发起登录请求并返回原始响应（用于断言登录失败场景）。</summary>
    private async Task<HttpResponseMessage> LoginAsync(string userName, string password, Guid? tenantId = null)
    {
        var (captchaId, captchaCode) = await GetCaptchaAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                userName,
                password,
                captchaId,
                captchaCode
            })
        };
        if (tenantId.HasValue)
        {
            request.Headers.Add(TenantResolutionMiddleware.TenantHeader, tenantId.Value.ToString());
        }

        return await _httpClient.SendAsync(request);
    }

    /// <summary>登录并返回 accessToken（可携带租户头）。</summary>
    private async Task<string> LoginAndGetAccessTokenAsync(string userName, string password, Guid? tenantId = null)
    {
        var (captchaId, captchaCode) = await GetCaptchaAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                userName,
                password,
                captchaId,
                captchaCode
            })
        };
        if (tenantId.HasValue)
        {
            request.Headers.Add(TenantResolutionMiddleware.TenantHeader, tenantId.Value.ToString());
        }

        var response = await _httpClient.SendAsync(request);
        var body = await ReadBodyAsync(response);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"登录应成功：{body.RootElement.GetRawText()}");
        return body.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    /// <summary>带令牌 + 租户头的请求。</summary>
    private async Task<HttpResponseMessage> SendAuthorizedWithTenantAsync(HttpMethod method, string url, string accessToken, Guid tenantId)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add(TenantResolutionMiddleware.TenantHeader, tenantId.ToString());
        return await _httpClient.SendAsync(request);
    }

    /// <summary>带令牌的请求。</summary>
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
        _cache.RemoveAsync(Application.Commands.AuthLoginCommandHandler.LoginFailCacheKeyPrefix + userName).GetAwaiter().GetResult();
    }

    /// <summary>读取响应正文为 JSON 文档。</summary>
    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
