
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using Application.Commands;
using Application.Contracts.Commands;
using Application.Contracts.Queries;
using Domain.Entities;
using Domain.Services.Auth;
using Domain.Services.Security;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace API.Tests;
/// <summary>
/// 系统管理（B5 用户 / 角色 / 部门 + B6 菜单）集成测试：
/// 走真实 HTTP 管道 + 真实 MySQL（默认库 super_admin_test，SUPER_ADMIN_TEST_CONN 可覆盖）。
/// </summary>
[TestFixture]
public class SystemManagementApiTests
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
                        ["Database:ConnectionString"] = connectionString
                    });
                });
            });

        _httpClient = _factory.CreateClient();
        _db = _factory.Services.GetRequiredService<ISqlSugarClient>();
        _cache = _factory.Services.GetRequiredService<ICache>();

        ClearLoginFailState(AdminUserName);
        _adminToken = await LoginAndGetAccessTokenAsync(AdminUserName, AdminPassword);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
        _db.Dispose();
        (_cache as IDisposable)?.Dispose();
        _factory.Dispose();
    }

    // —— 用户管理 ——

    [Test]
    public async Task UserPage_AsSuperAdmin_ReturnsPagedItemsWithDeptAndRoles()
    {
        // keyword 过滤锁定 admin：默认按创建时间倒序，其他用例动态创建的用户会更新，admin 未必落在第一页
        var body = await GetJsonAsync("/api/system/user/page?pageIndex=1&pageSize=5&keyword=admin");

        Assert.Multiple(() =>
        {
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(200));
            var data = body.RootElement.GetProperty("data");
            Assert.That(data.GetProperty("total").GetInt64(), Is.GreaterThanOrEqualTo(1));
            Assert.That(data.GetProperty("pageIndex").GetInt32(), Is.EqualTo(1));
            Assert.That(data.GetProperty("pageSize").GetInt32(), Is.EqualTo(5));
        });

        var items = body.RootElement.GetProperty("data").GetProperty("items");
        var admin = items.EnumerateArray().FirstOrDefault(u => u.GetProperty("userName").GetString() == AdminUserName);
        Assert.That(admin.ValueKind, Is.EqualTo(JsonValueKind.Object), "分页结果应包含 admin");
        Assert.Multiple(() =>
        {
            Assert.That(admin.GetProperty("nickName").GetString(), Is.EqualTo("超级管理员"));
            Assert.That(admin.GetProperty("deptName").GetString(), Is.EqualTo("总公司"));
            Assert.That(admin.GetProperty("roles").EnumerateArray()
                .Any(r => r.GetProperty("roleName").GetString() == "超级管理员"), Is.True, "列表应含角色名");
            Assert.That(admin.TryGetProperty("passwordHash", out _), Is.False, "列表不得泄露密码哈希");
        });
    }

    [Test]
    public async Task UserPage_KeywordFilter_MatchesUserName()
    {
        var userName = NewUserName();
        await CreateUserAsync(userName: userName, nickName: "关键字检索用户");
        var body = await GetJsonAsync($"/api/system/user/page?pageIndex=1&pageSize=50&keyword={userName}");

        var items = body.RootElement.GetProperty("data").GetProperty("items");
        Assert.Multiple(() =>
        {
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(200));
            Assert.That(items.GetArrayLength(), Is.EqualTo(1), "关键字应精确命中新建用户");
            Assert.That(items[0].GetProperty("userName").GetString(), Is.EqualTo(userName));
        });
    }

    [Test]
    public async Task User_CreateUpdateDelete_FullFlow()
    {
        var roleId = await CreateRoleAsync(menuIds: []);
        var userName = NewUserName();

        // 创建（带角色）
        var createBody = await SendForJsonAsync(HttpMethod.Post, "/api/system/user", new
        {
            userName,
            nickName = "流程测试用户",
            password = "init@123",
            deptId = SeedId(101),
            email = "flow@example.com",
            phone = "13900001111",
            status = 1,
            description = "B5 流程测试",
            roleIds = new Guid[] { roleId }
        }, _adminToken);
        var userId = await ApiTestSupport.GetEntityIdAsync<SysUser>(_db, it => it.UserName == userName);

        // 详情：字段与角色回显
        var detail = await GetJsonAsync($"/api/system/user/{userId}");
        var detailData = detail.RootElement.GetProperty("data");
        Assert.Multiple(() =>
        {
            Assert.That(detailData.GetProperty("userName").GetString(), Is.EqualTo(userName));
            Assert.That(detailData.GetProperty("deptId").GetGuid(), Is.EqualTo(SeedId(101)));
            Assert.That(detailData.GetProperty("roleIds").EnumerateArray().Select(r => r.GetGuid()).Contains(roleId),
                Is.True, "详情应回显角色 Id");
        });

        // 更新（角色清空、昵称变更；乐观锁：先回读版本号）
        var userVersion = await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId);
        await SendForJsonAsync(HttpMethod.Put, $"/api/system/user/{userId}", new
        {
            version = userVersion,
            nickName = "流程测试用户V2",
            deptId = SeedId(102),
            email = "flow2@example.com",
            phone = "13900002222",
            status = 0,
            description = "更新后",
            roleIds = Array.Empty<Guid>()
        }, _adminToken);

        var updated = (await GetJsonAsync($"/api/system/user/{userId}")).RootElement.GetProperty("data");
        Assert.Multiple(() =>
        {
            Assert.That(updated.GetProperty("nickName").GetString(), Is.EqualTo("流程测试用户V2"));
            Assert.That(updated.GetProperty("deptId").GetGuid(), Is.EqualTo(SeedId(102)));
            Assert.That(updated.GetProperty("roleIds").GetArrayLength(), Is.EqualTo(0), "角色应被全量替换为空");
        });

        // 删除后再查：用户不存在
        var deleteResponse = await SendAsync(HttpMethod.Delete, $"/api/system/user/{userId}", new { version = await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId) }, _adminToken);
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var missing = await SendAsync(HttpMethod.Get, $"/api/system/user/{userId}", null, _adminToken);
        var missingBody = await ReadBodyAsync(missing);
        Assert.Multiple(() =>
        {
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(missingBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400201));
        });

        // 清理角色
        await SendAsync(HttpMethod.Delete, $"/api/system/role/{roleId}", new { version = await ApiTestSupport.GetVersionAsync<SysRole>(_db, roleId) }, _adminToken);
    }

    [Test]
    public async Task User_Create_DuplicateUserName_Returns400204()
    {
        var userName = NewUserName();
        await CreateUserAsync(userName: userName);

        var response = await SendAsync(HttpMethod.Post, "/api/system/user", new
        {
            userName,
            nickName = "重复用户名",
            status = 1
        }, _adminToken);
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400204));
        });
    }

    [Test]
    public async Task User_ResetPassword_NewPasswordCanLogin()
    {
        var userName = NewUserName();
        await CreateUserAsync(userName: userName, password: "init@123");

        var user = (await _db.Queryable<SysUser>().Where(u => u.UserName == userName).ToListAsync()).Single();
        var resetResponse = await SendAsync(HttpMethod.Put, $"/api/system/user/{user.Id}/password", new
        {
            version = await ApiTestSupport.GetVersionAsync<SysUser>(_db, user.Id),
            password = "newPwd@456"
        }, _adminToken);
        Assert.That(resetResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // 旧密码登录失败
        var oldLogin = await TryLoginAsync(userName, "init@123");
        Assert.That(oldLogin.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "旧密码应失效");

        // 新密码登录成功（走真实登录链路：验证码 + BCrypt 校验）
        var newLogin = await TryLoginAsync(userName, "newPwd@456");
        Assert.That(newLogin.StatusCode, Is.EqualTo(HttpStatusCode.OK), "重置后新密码应可登录");
    }

    [Test]
    public async Task User_Create_DefaultPassword_123456_CanLogin()
    {
        // 创建时不传 password → 缺省 123456
        var userName = NewUserName();
        await CreateUserAsync(userName: userName, password: null);
        var response = await TryLoginAsync(userName, "123456");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "缺省密码应为 123456");
    }

    [Test]
    public async Task User_AdminProtectedOperations_Returns400205()
    {
        // admin 固定 Id=1（种子）
        var update = await SendAsync(HttpMethod.Put, $"/api/system/user/{SeedId(1)}", new
        {
            nickName = "改名尝试",
            deptId = SeedId(101),
            email = (string?)null,
            phone = (string?)null,
            status = 1,
            description = (string?)null,
            roleIds = Array.Empty<Guid>()
        }, _adminToken);
        var updateBody = await ReadBodyAsync(update);

        var status = await SendAsync(HttpMethod.Put, $"/api/system/user/{SeedId(1)}/status", new { status = 0 }, _adminToken);
        var statusBody = await ReadBodyAsync(status);

        var delete = await SendAsync(HttpMethod.Delete, $"/api/system/user/{SeedId(1)}", new { version = Guid.Empty }, _adminToken);
        var deleteBody = await ReadBodyAsync(delete);

        Assert.Multiple(() =>
        {
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(updateBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400205));
            Assert.That(status.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(statusBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400205));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(deleteBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400205));
        });
    }

    // —— 数据范围过滤 ——

    [Test]
    public async Task UserPage_WithDeptScopeRole_SeesOnlyOwnDeptUsers()
    {
        // 造角色：数据范围 = 本部门（3），授权用户管理菜单（目录 100 + 菜单 101 + 查询按钮 1001）
        var roleId = await CreateRoleAsync(dataScope: 3, menuIds: [SeedId(100), SeedId(101), SeedId(1001)]);

        // 本部门用户（研发部 101，带角色）与其他部门用户（市场部 102）
        var scopedUserName = NewUserName();
        var scopedUserId = await CreateUserAsync(
            userName: scopedUserName, nickName: "本部门范围用户", password: "scope@123", deptId: SeedId(101), roleIds: [roleId]);
        var otherUserName = NewUserName();
        await CreateUserAsync(userName: otherUserName, nickName: "其他部门用户", deptId: SeedId(102));

        // 本部门范围用户登录并查询用户分页
        var scopedToken = await LoginAndGetAccessTokenAsync(scopedUserName, "scope@123");

        // 校验权限：角色菜单已生效
        var userInfo = await GetJsonAsync("/api/auth/user-info", scopedToken);
        var permissions = userInfo.RootElement.GetProperty("data").GetProperty("permissions")
            .EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.That(permissions, Does.Contain("system:user:list"), "角色菜单授权应转化为权限标识");

        // 分页只应看到研发部（101）用户：看不到市场部用户，也看不到总公司 admin
        var page = await GetJsonAsync("/api/system/user/page?pageIndex=1&pageSize=50", scopedToken);
        var items = page.RootElement.GetProperty("data").GetProperty("items");
        var deptIds = items.EnumerateArray()
            .Select(u => u.GetProperty("deptId").GetGuid())
            .Distinct()
            .ToList();
        var userNames = items.EnumerateArray()
            .Select(u => u.GetProperty("userName").GetString())
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(items.GetArrayLength(), Is.GreaterThanOrEqualTo(1));
            Assert.That(deptIds.Contains(SeedId(101)), Is.True, "应能看到本部门用户");
            Assert.That(deptIds, Has.All.EqualTo(SeedId(101)), "只应看到本部门（101）用户");
            Assert.That(userNames, Does.Contain(scopedUserName), "本部门范围用户应看到自己");
            Assert.That(userNames, Does.Not.Contain(otherUserName), "不应看到其他部门用户");
            Assert.That(userNames, Does.Not.Contain(AdminUserName), "不应看到其他部门（总公司）的 admin");
        });
    }

    // —— 部门管理 ——

    [Test]
    public async Task Dept_CreateTreeDetail()
    {
        var deptName = "研发一组" + Guid.NewGuid().ToString("N")[..4];
        await SendForJsonAsync(HttpMethod.Post, "/api/system/dept", new
        {
            parentId = SeedId(101),
            deptName,
            orderNum = 1,
            leader = "组长",
            status = 1
        }, _adminToken);
        var deptId = await ApiTestSupport.GetEntityIdAsync<SysDept>(_db, it => it.DeptName == deptName);

        var detail = (await GetJsonAsync($"/api/system/dept/{deptId}")).RootElement.GetProperty("data");
        Assert.Multiple(() =>
        {
            Assert.That(detail.GetProperty("parentId").GetGuid(), Is.EqualTo(SeedId(101)));
            Assert.That(detail.GetProperty("status").GetInt32(), Is.EqualTo(1));
        });

        // 树中应包含新节点
        var tree = await GetJsonAsync("/api/system/dept/tree", _adminToken);
        var rd = tree.RootElement.GetProperty("data").EnumerateArray()
            .SelectMany(root => root.GetProperty("children").EnumerateArray())
            .First(d => d.GetProperty("id").GetGuid() == SeedId(101));
        Assert.That(rd.GetProperty("children").EnumerateArray()
            .Any(c => c.GetProperty("id").GetGuid() == deptId), Is.True, "部门树应包含新建子部门");

        // 清理（无子级无用户，可直接删除）
        await SendAsync(HttpMethod.Delete, $"/api/system/dept/{deptId}", new { version = await ApiTestSupport.GetVersionAsync<SysDept>(_db, deptId) }, _adminToken);
    }

    [Test]
    public async Task Dept_Update_ParentAsOwnDescendant_Returns400504()
    {
        // 构造 D1（研发部下）与 D2（D1 下），尝试把 D1 的父级设为 D2
        var d1Name = "层级校验A" + Guid.NewGuid().ToString("N")[..4];
        var d2Name = "层级校验B" + Guid.NewGuid().ToString("N")[..4];
        await SendForJsonAsync(HttpMethod.Post, "/api/system/dept", new
        {
            parentId = SeedId(101),
            deptName = d1Name,
            orderNum = 9,
            status = 1
        }, _adminToken);
        var d1 = await ApiTestSupport.GetEntityIdAsync<SysDept>(_db, it => it.DeptName == d1Name);
        await SendForJsonAsync(HttpMethod.Post, "/api/system/dept", new
        {
            parentId = d1,
            deptName = d2Name,
            orderNum = 9,
            status = 1
        }, _adminToken);
        var d2 = await ApiTestSupport.GetEntityIdAsync<SysDept>(_db, it => it.DeptName == d2Name);

        var d1Version = await ApiTestSupport.GetVersionAsync<SysDept>(_db, d1);
        var response = await SendAsync(HttpMethod.Put, $"/api/system/dept/{d1}", new
        {
            version = d1Version,
            parentId = d2,
            deptName = "层级校验A",
            orderNum = 9,
            status = 1
        }, _adminToken);
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400504));
        });

        // 清理
        await SendAsync(HttpMethod.Delete, $"/api/system/dept/{d2}", new { version = await ApiTestSupport.GetVersionAsync<SysDept>(_db, d2) }, _adminToken);
        await SendAsync(HttpMethod.Delete, $"/api/system/dept/{d1}", new { version = await ApiTestSupport.GetVersionAsync<SysDept>(_db, d1) }, _adminToken);
    }

    [Test]
    public async Task Dept_Delete_WithChildrenOrUsers_Rejected()
    {
        // 有子部门：总公司（100）下存在研发部 / 市场部
        var withChildren = await SendAsync(HttpMethod.Delete, $"/api/system/dept/{SeedId(100)}", new { version = Guid.Empty }, _adminToken);
        var withChildrenBody = await ReadBodyAsync(withChildren);
        Assert.Multiple(() =>
        {
            Assert.That(withChildren.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(withChildrenBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400502));
        });

        // 有用户：研发部（101）下创建一个用户后删除部门被拒；删除用户后可正常删除
        var deptName = "待删部门" + Guid.NewGuid().ToString("N")[..4];
        await SendForJsonAsync(HttpMethod.Post, "/api/system/dept", new
        {
            parentId = SeedId(101),
            deptName,
            orderNum = 8,
            status = 1
        }, _adminToken);
        var deptId = await ApiTestSupport.GetEntityIdAsync<SysDept>(_db, it => it.DeptName == deptName);
        var userId = await CreateUserAsync(nickName: "部门占用用户", deptId: deptId);

        var withUser = await SendAsync(HttpMethod.Delete, $"/api/system/dept/{deptId}", new { version = Guid.Empty }, _adminToken);
        var withUserBody = await ReadBodyAsync(withUser);
        Assert.Multiple(() =>
        {
            Assert.That(withUser.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(withUserBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400503));
        });

        // 移除占用用户（软删除）后部门可删除
        await SendAsync(HttpMethod.Delete, $"/api/system/user/{userId}", new { version = await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId) }, _adminToken);
        var after = await SendAsync(HttpMethod.Delete, $"/api/system/dept/{deptId}", new { version = await ApiTestSupport.GetVersionAsync<SysDept>(_db, deptId) }, _adminToken);
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.OK), "占用解除后应可删除部门");
    }

    // —— 角色管理与权限联动 ——

    [Test]
    public async Task Role_AssignMenus_UserPermissionsChangeAndEcho()
    {
        var roleKey = NewRoleKey();
        var roleId = await CreateRoleAsync(roleKey: roleKey, menuIds: []);
        var userName = NewUserName();
        var userId = await CreateUserAsync(userName: userName, nickName: "权限联动用户", password: "perm@123", roleIds: [roleId]);

        // 初始：角色无菜单 → 权限为空
        var token = await LoginAndGetAccessTokenAsync(userName, "perm@123");
        var before = await GetJsonAsync("/api/auth/user-info", token);
        Assert.That(before.RootElement.GetProperty("data").GetProperty("permissions").GetArrayLength(),
            Is.EqualTo(0), "无菜单角色用户权限应为空");

        // 授权菜单（目录 100 + 菜单 101 + 查询按钮 1001）→ user-info permissions 立即变化
        var grantVersion = await ApiTestSupport.GetVersionAsync<SysRole>(_db, roleId);
        await SendForJsonAsync(HttpMethod.Put, $"/api/system/role/{roleId}", new
        {
            version = grantVersion,
            roleName = "权限联动角色",
            roleKey,
            dataScope = 1,
            status = 1,
            menuIds = new Guid[] { SeedId(100), SeedId(101), SeedId(1001) }
        }, _adminToken);

        var after = await GetJsonAsync("/api/auth/user-info", token);
        var perms = after.RootElement.GetProperty("data").GetProperty("permissions")
            .EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(perms, Does.Contain("system:user:list"), "授权菜单后应出现权限标识");
            Assert.That(after.RootElement.GetProperty("data").GetProperty("roles").EnumerateArray()
                .Select(r => r.GetString()), Does.Contain(roleKey));
        });

        // 角色详情回显 menuIds
        var detail = (await GetJsonAsync($"/api/system/role/{roleId}", _adminToken)).RootElement.GetProperty("data");
        var menuIds = detail.GetProperty("menuIds").EnumerateArray().Select(m => m.GetGuid()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(menuIds.Contains(SeedId(100)), Is.True, "详情 menuIds 应回显目录");
            Assert.That(menuIds.Contains(SeedId(101)), Is.True, "详情 menuIds 应回显菜单");
            Assert.That(menuIds.Contains(SeedId(1001)), Is.True, "详情 menuIds 应回显按钮");
        });

        // 角色已被用户引用：删除被拒（400305）
        var deleteInUse = await SendAsync(HttpMethod.Delete, $"/api/system/role/{roleId}", new { version = Guid.Empty }, _adminToken);
        var deleteInUseBody = await ReadBodyAsync(deleteInUse);
        Assert.Multiple(() =>
        {
            Assert.That(deleteInUse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(deleteInUseBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400305));
        });

        // 清空菜单 → 权限再次变化为空
        var clearVersion = await ApiTestSupport.GetVersionAsync<SysRole>(_db, roleId);
        await SendForJsonAsync(HttpMethod.Put, $"/api/system/role/{roleId}", new
        {
            version = clearVersion,
            roleName = "权限联动角色",
            roleKey,
            dataScope = 1,
            status = 1,
            menuIds = Array.Empty<Guid>()
        }, _adminToken);
        var cleared = await GetJsonAsync("/api/auth/user-info", token);
        Assert.That(cleared.RootElement.GetProperty("data").GetProperty("permissions").GetArrayLength(),
            Is.EqualTo(0), "清空角色菜单后权限应立即失效");

        // 清理：删除用户后角色可删除
        await SendAsync(HttpMethod.Delete, $"/api/system/user/{userId}", new { version = await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId) }, _adminToken);
        var deleteRole = await SendAsync(HttpMethod.Delete, $"/api/system/role/{roleId}", new { version = await ApiTestSupport.GetVersionAsync<SysRole>(_db, roleId) }, _adminToken);
        Assert.That(deleteRole.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Role_Create_RulesAndValidation()
    {
        var roleKey = NewRoleKey();
        var roleId = await CreateRoleAsync(roleKey: roleKey);

        // 保留 key：super_admin
        var reserved = await SendAsync(HttpMethod.Post, "/api/system/role", new
        {
            roleName = "保留key",
            roleKey = "super_admin",
            dataScope = 1,
            status = 1
        }, _adminToken);
        var reservedBody = await ReadBodyAsync(reserved);

        // 重复 key
        var duplicate = await SendAsync(HttpMethod.Post, "/api/system/role", new
        {
            roleName = "重复key",
            roleKey,
            dataScope = 1,
            status = 1
        }, _adminToken);
        var duplicateBody = await ReadBodyAsync(duplicate);

        Assert.Multiple(() =>
        {
            Assert.That(reserved.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(reservedBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400303));
            Assert.That(duplicate.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(duplicateBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400302));
        });

        await SendAsync(HttpMethod.Delete, $"/api/system/role/{roleId}", new { version = await ApiTestSupport.GetVersionAsync<SysRole>(_db, roleId) }, _adminToken);
    }

    [Test]
    public async Task Role_UpdateAndDelete_SuperAdminProtected_Returns400304()
    {
        var update = await SendAsync(HttpMethod.Put, $"/api/system/role/{SeedId(1)}", new
        {
            roleName = "尝试改名",
            roleKey = "super_admin",
            dataScope = 1,
            status = 1,
            menuIds = Array.Empty<Guid>()
        }, _adminToken);
        var updateBody = await ReadBodyAsync(update);

        var delete = await SendAsync(HttpMethod.Delete, $"/api/system/role/{SeedId(1)}", new { version = Guid.Empty }, _adminToken);
        var deleteBody = await ReadBodyAsync(delete);

        Assert.Multiple(() =>
        {
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(updateBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400304));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(deleteBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400304));
        });
    }

    [Test]
    public async Task Role_All_ContainsSuperAdminForUserDropdown()
    {
        var body = await GetJsonAsync("/api/system/role/all", _adminToken);
        var keys = body.RootElement.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("roleKey").GetString()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(200));
            Assert.That(keys, Does.Contain("super_admin"), "下拉应包含超管角色");
            Assert.That(keys, Does.Not.Contain((string?)null));
        });
    }

    // —— 菜单管理 ——

    [Test]
    public async Task Menu_CreateUpdateDelete_FlowWithValidation()
    {
        // 校验：目录缺 path
        var invalidDirectory = await SendAsync(HttpMethod.Post, "/api/system/menu", new
        {
            parentId = Guid.Empty,
            menuType = 1,
            menuName = "非法目录",
            path = " ",
            status = 1
        }, _adminToken);
        var invalidDirectoryBody = await ReadBodyAsync(invalidDirectory);
        Assert.Multiple(() =>
        {
            Assert.That(invalidDirectory.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(invalidDirectoryBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400404));
        });

        // 校验：按钮缺 perms
        var invalidButton = await SendAsync(HttpMethod.Post, "/api/system/menu", new
        {
            parentId = SeedId(101),
            menuType = 3,
            menuName = "非法按钮",
            path = "#",
            status = 1
        }, _adminToken);
        var invalidButtonBody = await ReadBodyAsync(invalidButton);
        Assert.That(invalidButtonBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400404));

        // 正常创建按钮（挂在用户管理 101 下）
        var perms = $"system:user:export{Guid.NewGuid().ToString("N")[..6]}";
        var createBody = await SendForJsonAsync(HttpMethod.Post, "/api/system/menu", new
        {
            parentId = SeedId(101),
            menuType = 3,
            menuName = "用户导出测试",
            path = "#",
            perms,
            orderNum = 9,
            visible = 1,
            status = 1
        }, _adminToken);
        var menuId = await ApiTestSupport.GetEntityIdAsync<SysMenu>(_db, it => it.Perms == perms);

        var detail = (await GetJsonAsync($"/api/system/menu/{menuId}", _adminToken)).RootElement.GetProperty("data");
        Assert.Multiple(() =>
        {
            Assert.That(detail.GetProperty("menuType").GetInt32(), Is.EqualTo(3));
            Assert.That(detail.GetProperty("perms").GetString(), Is.EqualTo(perms));
        });

        // 父级设为自身 → 400405
        var menuVersion = await ApiTestSupport.GetVersionAsync<SysMenu>(_db, menuId);
        var parentSelf = await SendAsync(HttpMethod.Put, $"/api/system/menu/{menuId}", new
        {
            version = menuVersion,
            parentId = menuId,
            menuType = 3,
            menuName = "用户导出测试",
            path = "#",
            perms,
            orderNum = 9,
            visible = 1,
            status = 1
        }, _adminToken);
        var parentSelfBody = await ReadBodyAsync(parentSelf);
        Assert.That(parentSelfBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400405));

        // 父级设为自己的后代（101 的子级 1001）→ 400405
        var menu101Version = await ApiTestSupport.GetVersionAsync<SysMenu>(_db, SeedId(101));
        var parentDescendant = await SendAsync(HttpMethod.Put, $"/api/system/menu/{SeedId(101)}", new
        {
            version = menu101Version,
            parentId = SeedId(1001),
            menuType = 2,
            menuName = "用户管理",
            path = "user",
            component = "system/user/index",
            perms = "system:user:list",
            orderNum = 1,
            visible = 1,
            keepAlive = false,
            status = 1
        }, _adminToken);
        var parentDescendantBody = await ReadBodyAsync(parentDescendant);
        Assert.That(parentDescendantBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400405));

        // 删除有子菜单的节点 → 400402
        var withChildren = await SendAsync(HttpMethod.Delete, $"/api/system/menu/{SeedId(100)}", new { version = Guid.Empty }, _adminToken);
        var withChildrenBody = await ReadBodyAsync(withChildren);
        Assert.Multiple(() =>
        {
            Assert.That(withChildren.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(withChildrenBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400402));
        });

        // 已被角色引用的菜单删除 → 400403
        var roleId = await CreateRoleAsync(menuIds: [menuId]);
        var inUse = await SendAsync(HttpMethod.Delete, $"/api/system/menu/{menuId}", new { version = Guid.Empty }, _adminToken);
        var inUseBody = await ReadBodyAsync(inUse);
        Assert.Multiple(() =>
        {
            Assert.That(inUse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(inUseBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400403));
        });

        // 清理：删除角色后再删菜单
        await SendAsync(HttpMethod.Delete, $"/api/system/role/{roleId}", new { version = await ApiTestSupport.GetVersionAsync<SysRole>(_db, roleId) }, _adminToken);
        var deleted = await SendAsync(HttpMethod.Delete, $"/api/system/menu/{menuId}", new { version = await ApiTestSupport.GetVersionAsync<SysMenu>(_db, menuId) }, _adminToken);
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // 菜单树应可查询且包含种子菜单
        var tree = await GetJsonAsync("/api/system/menu/tree", _adminToken);
        var names = tree.RootElement.GetProperty("data").EnumerateArray()
            .Select(m => m.GetProperty("menuName").GetString()).ToList();
        Assert.That(names, Does.Contain("系统管理"));
    }

    // —— 测试辅助 ——

    /// <summary>生成唯一用户名。</summary>
    private static string NewUserName() => "b5" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>生成唯一角色 key。</summary>
    private static string NewRoleKey() => "b5" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>由序号生成种子占位 Guid（与 SeedDataInitializer 种子主键同格式）。</summary>
    private static Guid SeedId(long n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    /// <summary>以 admin 创建用户（可指定用户名 / 昵称 / 密码 / 部门 / 角色），返回用户 Id。</summary>
    private async Task<Guid> CreateUserAsync(
        string? userName = null,
        string nickName = "B5 测试用户",
        string? password = "test@123",
        Guid? deptId = null,
        List<Guid>? roleIds = null)
    {
        userName ??= NewUserName();
        var response = await SendAsync(HttpMethod.Post, "/api/system/user", new
        {
            userName,
            nickName,
            password,
            deptId,
            status = 1,
            roleIds = roleIds ?? []
        }, _adminToken);

        var body = await ReadBodyAsync(response);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"创建用户应成功：{body.RootElement.GetProperty("message").GetString()}");
        return await ApiTestSupport.GetEntityIdAsync<SysUser>(_db, it => it.UserName == userName);
    }

    /// <summary>以 admin 创建角色，返回角色 Id。</summary>
    private async Task<Guid> CreateRoleAsync(int dataScope = 1, List<Guid>? menuIds = null, string? roleKey = null)
    {
        roleKey ??= NewRoleKey();
        var response = await SendAsync(HttpMethod.Post, "/api/system/role", new
        {
            roleName = "B5 测试角色",
            roleKey,
            dataScope,
            status = 1,
            menuIds = menuIds ?? []
        }, _adminToken);

        var body = await ReadBodyAsync(response);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"创建角色应成功：{body.RootElement.GetProperty("message").GetString()}");
        return await ApiTestSupport.GetEntityIdAsync<SysRole>(_db, it => it.RoleKey == roleKey);
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

    /// <summary>登录（可能失败，返回原始响应，由调用方断言状态码）。</summary>
    private async Task<HttpResponseMessage> TryLoginAsync(string userName, string password)
    {
        var (captchaId, captchaCode) = await GetCaptchaAsync();
        return await PostJsonAsync("/api/auth/login", new
        {
            userName,
            password,
            captchaId,
            captchaCode
        });
    }

    /// <summary>登录并返回 accessToken。</summary>
    private async Task<string> LoginAndGetAccessTokenAsync(string userName, string password)
    {
        var response = await TryLoginAsync(userName, password);
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            Assert.Fail($"登录应成功：{userName}，HTTP {(int)response.StatusCode}，响应：{content}");
        }

        var body = await ReadBodyAsync(response);
        return body.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    /// <summary>清除指定用户名的登录失败计数。</summary>
    private void ClearLoginFailState(string userName)
    {
        _cache.RemoveAsync(AuthLoginCommandHandler.LoginFailCacheKeyPrefix + userName).GetAwaiter().GetResult();
    }

    /// <summary>POST JSON 请求。</summary>
    private async Task<HttpResponseMessage> PostJsonAsync(string url, object payload, string? accessToken = null)
    {
        return await SendAsync(HttpMethod.Post, url, payload, accessToken);
    }

    /// <summary>发送 JSON 请求（POST / PUT / DELETE 等）。</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? payload = null, string? accessToken = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = payload is null ? null : JsonContent.Create(payload)
        };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await _httpClient.SendAsync(request);
    }

    /// <summary>发送请求并把响应解析为 JsonDocument（断言成功状态）。</summary>
    private static async Task<JsonDocument> SendForJsonAsync(HttpMethod method, string url, object payload, string accessToken)
    {
        var response = await SendAsync(method, url, payload, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True, $"请求应成功：{method} {url}，响应：{content}");
        return JsonDocument.Parse(content);
    }

    /// <summary>GET 并解析响应（断言成功状态）。</summary>
    private static async Task<JsonDocument> GetJsonAsync(string url, string? accessToken = null)
    {
        return await SendForJsonAsync(HttpMethod.Get, url, null!, accessToken ?? _adminToken);
    }

    /// <summary>读取响应体为 JsonDocument。</summary>
    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
