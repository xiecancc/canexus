using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
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
/// 用户写操作数据范围守卫（S2）集成测试——多租户关闭（保持平台原行为）：
/// 受限角色（DataScope=Dept）对范围外用户执行更新 / 删除 / 恢复 / 改状态 / 重置密码 / 分配角色
/// 均返回 403（40300），范围内用户正常成功；平台超管不受数据范围限制。
/// 走真实 HTTP 管道 + 真实 MySQL（super_admin_test）。
/// </summary>
[TestFixture]
public class UserDataScopeApiTests
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";
    private const string OperatorPassword = "test@123";
    private const string UserNamePrefix = "scope-";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static ICache _cache = null!;
    private static IPasswordHasher _passwordHasher = null!;
    private static string _adminToken = null!;

    private readonly List<Guid> _createdUserIds = [];
    private readonly List<string> _createdUserNames = [];
    private readonly List<Guid> _createdDeptIds = [];
    private readonly List<Guid> _createdMenuIds = [];

    private Guid _deptA = Guid.Empty;
    private Guid _deptB = Guid.Empty;
    private Guid _roleId = Guid.Empty;
    private string _operatorUserName = string.Empty;
    private Guid _operatorUserId = Guid.Empty;
    private string _operatorToken = string.Empty;

    /// <summary>测试套件初始化：启动测试宿主（多租户默认关闭）并以平台 admin 登录。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var apiContentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "API"));
        _factory = new ApiTestFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddJsonFile(Path.Combine(apiContentRoot, "appsettings.json"), optional: false, reloadOnChange: false);
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = ApiTestSupport.TestConnectionString,
                        ["Jwt:Secret"] = ApiTestSupport.TestJwtSecret,
                        ["RateLimit:Enabled"] = "false"
                        // 不覆盖 MultiTenancy:Enabled（保持默认 false，验证多租户关闭时原行为不受影响）
                    });
                });
            });

        _httpClient = _factory.CreateClient();
        _db = _factory.Services.GetRequiredService<ISqlSugarClient>();
        _cache = _factory.Services.GetRequiredService<ICache>();
        _passwordHasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        ClearLoginFailState(AdminUserName);
        _adminToken = await LoginAndGetAccessTokenAsync(AdminUserName, AdminPassword);

        _deptA = await CreateDeptAsync("A");
        _deptB = await CreateDeptAsync("B");
        _roleId = await CreateRestrictedRoleAsync();

        _operatorUserName = $"{UserNamePrefix}operator-{Guid.NewGuid():N}"[..Math.Min(24, UserNamePrefix.Length + 17)];
        var operatorUser = await CreateUserAsync(_operatorUserName, _deptA);
        _operatorUserId = operatorUser.Id;
        await _db.Insertable(new SysUserRole { UserId = _operatorUserId, RoleId = _roleId }).ExecuteCommandAsync();

        ClearLoginFailState(_operatorUserName);
        _operatorToken = await LoginAndGetAccessTokenAsync(_operatorUserName, OperatorPassword);
    }

    /// <summary>测试套件清理：删除本套件创建的部门 / 角色 / 菜单 / 用户及其关联数据。</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync()
    {
        await Task.CompletedTask;
        _db.Deleteable<SysRefreshToken>().Where(r => _createdUserIds.Contains(r.UserId)).ExecuteCommand();
        _db.Deleteable<SysLoginLog>().Where(l => _createdUserNames.Contains(l.UserName)).ExecuteCommand();
        _db.Deleteable<SysUserRole>().Where(ur => ur.RoleId == _roleId).ExecuteCommand();
        _db.Deleteable<SysUser>().Where(u => _createdUserIds.Contains(u.Id)).ExecuteCommand();
        _db.Deleteable<SysRoleMenu>().Where(rm => rm.RoleId == _roleId).ExecuteCommand();
        _db.Deleteable<SysRole>().Where(r => r.Id == _roleId).ExecuteCommand();
        _db.Deleteable<SysMenu>().Where(m => _createdMenuIds.Contains(m.Id)).ExecuteCommand();
        _db.Deleteable<SysDept>().Where(d => _createdDeptIds.Contains(d.Id)).ExecuteCommand();

        _httpClient.Dispose();
        _db.Dispose();
        (_cache as IDisposable)?.Dispose();
        _factory.Dispose();
    }

    /// <summary>受限角色（本部门）对范围外用户的更新 / 改状态 / 重置密码 / 分配角色均返回 403。</summary>
    [Test]
    public async Task RestrictedOperator_OutOfScopeUser_WritesForbidden()
    {
        var target = await CreateUserAsync(NewUserName("out-rw"), _deptB);
        var version = await GetVersionAsync(target.Id);
        var payload = UserPayload("范围外用户", _deptB);

        var update = await SendAsOperatorAsync(HttpMethod.Put, $"/api/system/user/{target.Id}/{version}", payload);
        var status = await SendAsOperatorAsync(HttpMethod.Put, $"/api/system/user/{target.Id}/{version}/status", new { status = CommonStatus.Disabled });
        var password = await SendAsOperatorAsync(HttpMethod.Put, $"/api/system/user/{target.Id}/{version}/password", new { password = "Abcd1234" });
        var roles = await SendAsOperatorAsync(HttpMethod.Put, $"/api/system/user/{target.Id}/{version}/roles", new { roleIds = Array.Empty<Guid>() });

        Assert.Multiple(() =>
        {
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "范围外更新应 403");
            Assert.That(status.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "范围外改状态应 403");
            Assert.That(password.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "范围外重置密码应 403");
            Assert.That(roles.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "范围外分配角色应 403");
        });

        await AssertForbiddenCodeAsync(update);
        await AssertForbiddenCodeAsync(status);
        await AssertForbiddenCodeAsync(password);
        await AssertForbiddenCodeAsync(roles);
    }

    /// <summary>受限角色对范围外用户的删除返回 403，且目标用户未被软删除。</summary>
    [Test]
    public async Task RestrictedOperator_OutOfScopeUser_DeleteForbidden()
    {
        var target = await CreateUserAsync(NewUserName("out-del"), _deptB);
        var version = await GetVersionAsync(target.Id);

        var response = await SendAsOperatorAsync(HttpMethod.Delete, $"/api/system/user/{target.Id}/{version}");
        await AssertForbiddenCodeAsync(response);

        var row = await _db.Queryable<SysUser>().Where(u => u.Id == target.Id).FirstAsync();
        Assert.That(row, Is.Not.Null, "越界删除不应实际生效");
    }

    /// <summary>受限角色对范围外软删除用户的恢复返回 403，且目标仍处于墓碑状态。</summary>
    [Test]
    public async Task RestrictedOperator_OutOfScopeUser_RestoreForbidden()
    {
        var target = await CreateUserAsync(NewUserName("out-res"), _deptB);
        await SoftDeleteUserAsync(target.Id);

        var response = await SendAsOperatorAsync(HttpMethod.Patch,
            $"/api/system/user/{target.Id}/{await GetTombstoneVersionAsync(target.Id)}/restore");
        await AssertForbiddenCodeAsync(response);

        var tombstone = await _db.Queryable<SysUser>()
            .ClearFilter<SysUser>()
            .Where(u => u.Id == target.Id)
            .FirstAsync();
        Assert.That(tombstone, Is.Not.Null, "越界恢复不应把墓碑变为可见");
        Assert.That(tombstone!.DeletedAt, Is.Not.Null, "越界恢复不应解除软删除");
    }

    /// <summary>受限角色对范围内（本部门）用户的更新 / 改状态 / 重置密码 / 分配角色 / 删除均成功。</summary>
    [Test]
    public async Task RestrictedOperator_InScopeUser_WritesSucceed()
    {
        var target = await CreateUserAsync(NewUserName("in-rw"), _deptA);

        var update = await SendAsOperatorAsync(HttpMethod.Put, $"/api/system/user/{target.Id}/{await GetVersionAsync(target.Id)}", UserPayload("范围内用户", _deptA));
        var status = await SendAsOperatorAsync(HttpMethod.Put, $"/api/system/user/{target.Id}/{await GetVersionAsync(target.Id)}/status", new { status = CommonStatus.Disabled });
        var password = await SendAsOperatorAsync(HttpMethod.Put, $"/api/system/user/{target.Id}/{await GetVersionAsync(target.Id)}/password", new { password = "Abcd1234" });
        var roles = await SendAsOperatorAsync(HttpMethod.Put, $"/api/system/user/{target.Id}/{await GetVersionAsync(target.Id)}/roles", new { roleIds = Array.Empty<Guid>() });
        var delete = await SendAsOperatorAsync(HttpMethod.Delete, $"/api/system/user/{target.Id}/{await GetVersionAsync(target.Id)}");

        Assert.Multiple(() =>
        {
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), "范围内更新应成功");
            Assert.That(status.StatusCode, Is.EqualTo(HttpStatusCode.OK), "范围内改状态应成功");
            Assert.That(password.StatusCode, Is.EqualTo(HttpStatusCode.OK), "范围内重置密码应成功");
            Assert.That(roles.StatusCode, Is.EqualTo(HttpStatusCode.OK), "范围内分配角色应成功");
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), "范围内删除应成功");
        });
    }

    /// <summary>受限角色对范围内软删除用户的恢复成功。</summary>
    [Test]
    public async Task RestrictedOperator_InScopeUser_RestoreSucceeds()
    {
        var target = await CreateUserAsync(NewUserName("in-res"), _deptA);
        await SoftDeleteUserAsync(target.Id);

        // T-09 起恢复命令同样走 Version CAS，须回传墓碑当前版本而非随机占位值
        var response = await SendAsOperatorAsync(HttpMethod.Patch,
            $"/api/system/user/{target.Id}/{await GetTombstoneVersionAsync(target.Id)}/restore");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "范围内恢复应成功");

        var row = await _db.Queryable<SysUser>().Where(u => u.Id == target.Id).FirstAsync();
        Assert.That(row, Is.Not.Null, "恢复后应重新可见");
    }

    /// <summary>平台超管不受数据范围限制：可更新范围外（其他部门）用户。</summary>
    [Test]
    public async Task SuperAdmin_OutOfScopeUser_UpdateSucceeds()
    {
        var target = await CreateUserAsync(NewUserName("admin-rw"), _deptB);
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/system/user/{target.Id}/{await GetVersionAsync(target.Id)}")
        {
            Content = JsonContent.Create(UserPayload("超管更新范围外用户", _deptB))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        var response = await _httpClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "平台超管应保持原行为（不受数据范围限制）");
    }

    // —— 测试数据辅助 ——

    /// <summary>生成带唯一后缀的测试用户名。</summary>
    private static string NewUserName(string tag)
        => $"{UserNamePrefix}{tag}-{Guid.NewGuid():N}"[..Math.Min(30, UserNamePrefix.Length + tag.Length + 9)];

    /// <summary>创建一个用户（指定部门，密码为统一的测试密码）。</summary>
    private async Task<SysUser> CreateUserAsync(string userName, Guid? deptId)
    {
        var user = new SysUser
        {
            UserName = userName,
            NickName = "数据范围测试用户",
            PasswordHash = _passwordHasher.Hash(OperatorPassword),
            DeptId = deptId,
            Status = CommonStatus.Enabled
        };
        await _db.Insertable(user).ExecuteCommandAsync();
        _createdUserIds.Add(user.Id);
        _createdUserNames.Add(userName);
        return user;
    }

    /// <summary>直插一个测试部门（根节点）。</summary>
    private async Task<Guid> CreateDeptAsync(string suffix)
    {
        var dept = new SysDept
        {
            DeptName = $"数据范围测试部门{suffix}-{Guid.NewGuid():N}"[..Math.Min(30, 12 + suffix.Length + 9)],
            ParentId = SysDept.RootParentId,
            OrderNum = 1,
            Status = CommonStatus.Enabled
        };
        await _db.Insertable(dept).ExecuteCommandAsync();
        _createdDeptIds.Add(dept.Id);
        return dept.Id;
    }

    /// <summary>创建受限角色（DataScope=Dept）及用户写权限点（编辑 / 删除 / 恢复 / 重置密码）。</summary>
    private async Task<Guid> CreateRestrictedRoleAsync()
    {
        var role = new SysRole
        {
            RoleKey = $"scope_role_{Guid.NewGuid():N}",
            RoleName = "数据范围测试角色",
            DataScope = DataScope.Dept,
            Status = CommonStatus.Enabled
        };
        await _db.Insertable(role).ExecuteCommandAsync();

        var permissions = new[]
        {
            "system:user:edit",
            "system:user:remove",
            "system:user:restore",
            "system:user:resetPwd"
        };
        foreach (var permission in permissions)
        {
            var menu = new SysMenu
            {
                MenuName = $"数据范围测试权限-{permission}",
                Path = $"/scope-test/{permission}",
                MenuType = MenuType.Button,
                Perms = permission,
                Status = CommonStatus.Enabled
            };
            await _db.Insertable(menu).ExecuteCommandAsync();
            _createdMenuIds.Add(menu.Id);
            await _db.Insertable(new SysRoleMenu { RoleId = role.Id, MenuId = menu.Id }).ExecuteCommandAsync();
        }

        return role.Id;
    }

    /// <summary>读取用户当前乐观锁版本号（仅未删除行）。</summary>
    private async Task<Guid> GetVersionAsync(Guid userId)
    {
        var user = await _db.Queryable<SysUser>().Where(u => u.Id == userId).FirstAsync();
        Assert.That(user, Is.Not.Null, "目标用户应存在");
        return user!.Version;
    }

    /// <summary>读取软删除墓碑的当前 Version（临时清除全局软删除过滤器）。</summary>
    private async Task<Guid> GetTombstoneVersionAsync(Guid userId)
    {
        var user = await _db.Queryable<SysUser>()
            .ClearFilter<SysUser>()
            .Where(u => u.Id == userId)
            .FirstAsync();
        Assert.That(user, Is.Not.Null, "目标墓碑应存在");
        return user!.Version;
    }

    /// <summary>直插软删除墓碑（模拟已删除用户，供恢复守卫测试）。</summary>
    private Task SoftDeleteUserAsync(Guid userId)
    {
        _db.Updateable<SysUser>()
            .SetColumns(it => it.DeletedAt == DateTime.UtcNow)
            .Where(it => it.Id == userId)
            .ExecuteCommand();
        return Task.CompletedTask;
    }

    /// <summary>用户更新请求载荷（字段完整以通过验证器，确保请求抵达数据范围守卫）。</summary>
    private static object UserPayload(string nickName, Guid? deptId)
        => new
        {
            nickName,
            deptId,
            status = CommonStatus.Enabled,
            roleIds = Array.Empty<Guid>(),
            email = (string?)null,
            phone = (string?)null,
            description = (string?)null
        };

    /// <summary>以受限操作员身份发起写请求。</summary>
    private async Task<HttpResponseMessage> SendAsOperatorAsync(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _operatorToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _httpClient.SendAsync(request);
    }

    /// <summary>断言 403 且错误码为 AuthForbidden（40300，超出数据范围）。</summary>
    private static async Task AssertForbiddenCodeAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(content);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "越界写操作应返回 403");
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40300), "应返回无权限访问错误码");
        });
    }

    // —— HTTP 辅助 ——

    /// <summary>登录并返回 accessToken（不含租户头）。</summary>
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
