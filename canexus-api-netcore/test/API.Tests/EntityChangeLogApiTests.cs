
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
/// 实体变更日志（审计模块）集成测试：
/// 走真实 HTTP 管道 + 真实 MySQL（super_admin_test，SUPER_ADMIN_TEST_CONN 可覆盖），
/// 重点验证聚合更新产生字段级 old / new 变更记录（审计与并发控制字段不记录）、
/// 变更日志分页查询过滤，以及刷新令牌仓储等关闭开关的表不产生日志。
/// </summary>
[TestFixture]
public class EntityChangeLogApiTests
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
        _factory = new ApiTestFactory()
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

        // 测试隔离：清空历史变更日志，保证断言从已知状态开始
        await _db.Deleteable<SysEntityChangeLog>().ExecuteCommandAsync();

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

    // —— 变更记录 ——

    /// <summary>验证更新用户昵称产生字段级 old / new 变更记录，且审计字段（Version 等）不被记录。</summary>
    [Test]
    public async Task EntityChange_UserUpdate_FieldLevelLogged()
    {
        var userName = "echg" + Guid.NewGuid().ToString("N")[..12];
        var userId = await CreateUserAsync(userName);
        try
        {
            var oldNickName = (await _db.Queryable<SysUser>().Where(u => u.Id == userId).FirstAsync()).NickName;
            var newNickName = oldNickName + "-已变更";

            // 更新昵称（走聚合仓储 UpdateAsync，应产生 NickName 字段级变更日志）
            await SendForJsonAsync(HttpMethod.Put, $"/api/system/user/{userId}/{await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId)}", new
            {
                userName,
                nickName = newNickName,
                status = 1,
                roleIds = Array.Empty<Guid>()
            });

            var logs = await _db.Queryable<SysEntityChangeLog>()
                .Where(l => l.EntityName == nameof(SysUser) && l.EntityId == userId)
                .ToListAsync();

            // T-15 起新增也有 NickName 字段日志（OldValue=null），此处须取带旧值的更新记录
            var nickLog = logs
                .Where(l => l.FieldName == nameof(SysUser.NickName) && l.OldValue is not null)
                .OrderByDescending(l => l.ChangedAt)
                .FirstOrDefault();
            Assert.Multiple(() =>
            {
                Assert.That(nickLog, Is.Not.Null, "昵称变更应产生 NickName 字段级变更日志");
                Assert.That(nickLog!.OldValue, Is.EqualTo(oldNickName));
                Assert.That(nickLog.NewValue, Is.EqualTo(newNickName));
                Assert.That(nickLog.ChangedAt, Is.Not.Null, "应记录变更时间");
                Assert.That(logs.All(l => l.FieldName != nameof(SysUser.Version)), "乐观锁版本号不应被记录");
                Assert.That(logs.All(l => l.FieldName != nameof(SysUser.UpdatedAt)), "更新审计字段不应被记录");
                Assert.That(logs.All(l => l.FieldName != nameof(SysUser.CreatedAt)), "创建审计字段不应被记录");
            });
        }
        finally
        {
            await SendAsync(HttpMethod.Delete, $"/api/system/user/{userId}/{await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId)}", null);
        }
    }

    /// <summary>验证无字段变化的更新不产生变更日志（快照对比幂等）。</summary>
    [Test]
    public async Task EntityChange_UpdateWithoutValueChange_NoLogs()
    {
        var userName = "echg2" + Guid.NewGuid().ToString("N")[..12];
        var userId = await CreateUserAsync(userName);
        try
        {
            // T-15 起「新增」本身即产生字段级变更日志，故以更新前的基线数对比：
            // 原值回传（无任何业务字段变化）不应再新增日志
            var beforeCount = await _db.Queryable<SysEntityChangeLog>()
                .Where(l => l.EntityName == nameof(SysUser) && l.EntityId == userId)
                .CountAsync();
            Assert.That(beforeCount, Is.GreaterThan(0), "新增用户应留下字段级变更日志");

            var user = await _db.Queryable<SysUser>().Where(u => u.Id == userId).FirstAsync();
            await SendForJsonAsync(HttpMethod.Put, $"/api/system/user/{userId}/{user.Version}", new
            {
                userName,
                nickName = user.NickName,
                status = (int)user.Status,
                roleIds = Array.Empty<Guid>()
            });

            var count = await _db.Queryable<SysEntityChangeLog>()
                .Where(l => l.EntityName == nameof(SysUser) && l.EntityId == userId)
                .CountAsync();
            Assert.That(count, Is.EqualTo(beforeCount), "无字段变化不应新增变更日志");
        }
        finally
        {
            await SendAsync(HttpMethod.Delete, $"/api/system/user/{userId}/{await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId)}", null);
        }
    }

    /// <summary>T-15：新增 / 软删 / 恢复均产生字段级变更日志，且密码哈希列值不落明文。</summary>
    [Test]
    public async Task EntityChange_CreateDeleteRestore_AllProduceFieldLevelLogs()
    {
        var userName = "echglife" + Guid.NewGuid().ToString("N")[..10];
        var userId = await CreateUserAsync(userName);

        var created = await _db.Queryable<SysEntityChangeLog>()
            .Where(l => l.EntityName == nameof(SysUser) && l.EntityId == userId)
            .ToListAsync();
        var passwordLog = created.FirstOrDefault(l => l.FieldName == nameof(SysUser.PasswordHash));
        Assert.Multiple(() =>
        {
            Assert.That(created.Any(l => l.FieldName == nameof(SysUser.UserName) && l.OldValue is null),
                Is.True, "新增应逐字段记录 OldValue=null 的变更日志");
            Assert.That(passwordLog, Is.Not.Null, "密码哈希列也应出现在字段级日志中");
            Assert.That(passwordLog!.NewValue, Is.EqualTo("******"), "敏感字段值须脱敏");
            Assert.That(created.All(l => l.NewValue is null || !l.NewValue.Contains("Init@Change123")),
                Is.True, "明文口令不得落到变更日志");
        });

        // 软删除：IsDeleted False → True
        await SendAsync(HttpMethod.Delete,
            $"/api/system/user/{userId}/{await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId)}", null);
        var afterDelete = await _db.Queryable<SysEntityChangeLog>()
            .Where(l => l.EntityName == nameof(SysUser) && l.EntityId == userId && l.FieldName == nameof(SysUser.IsDeleted))
            .ToListAsync();
        Assert.That(afterDelete.Any(l => l.OldValue == "False" && l.NewValue == "True"),
            Is.True, "软删除应产生 IsDeleted 字段级变更日志");

        // 恢复：以墓碑当前 Version 发起（墓碑被软删除过滤器隐藏，需 ClearFilter 读取）
        var tombstone = await _db.Queryable<SysUser>().ClearFilter<SysUser>()
            .Where(u => u.Id == userId).FirstAsync();
        await SendForJsonAsync(HttpMethod.Patch, $"/api/system/user/{userId}/{tombstone.Version}/restore", null!);
        var afterRestore = await _db.Queryable<SysEntityChangeLog>()
            .Where(l => l.EntityName == nameof(SysUser) && l.EntityId == userId && l.FieldName == nameof(SysUser.IsDeleted))
            .ToListAsync();
        Assert.That(afterRestore.Any(l => l.OldValue == "True" && l.NewValue == "False"),
            Is.True, "恢复应产生 IsDeleted 字段级变更日志");

        // 清理：再次软删除，避免遗留启用账号影响其它用例
        await SendAsync(HttpMethod.Delete,
            $"/api/system/user/{userId}/{await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId)}", null);
    }

    // —— 变更日志查询 ——

    /// <summary>验证变更日志分页查询接口按实体与实体 Id 过滤。</summary>
    [Test]
    public async Task EntityChange_PageQuery_Filters()
    {
        var userName = "echg3" + Guid.NewGuid().ToString("N")[..12];
        var userId = await CreateUserAsync(userName);
        try
        {
            await SendForJsonAsync(HttpMethod.Put, $"/api/system/user/{userId}/{await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId)}", new
            {
                userName,
                nickName = "变更日志查询测试",
                status = 1,
                roleIds = Array.Empty<Guid>()
            });

            var page = await GetJsonAsync($"/api/system/entity-change/page?entityName=SysUser&entityId={userId}&pageSize=10");
            var data = page.RootElement.GetProperty("data");
            var items = data.GetProperty("items").EnumerateArray().ToList();
            Assert.That(items, Is.Not.Empty, "按实体 Id 过滤应命中变更日志");

            var row = items[0];
            Assert.Multiple(() =>
            {
                Assert.That(page.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(200));
                Assert.That(row.GetProperty("entityName").GetString(), Is.EqualTo(nameof(SysUser)));
                Assert.That(row.GetProperty("entityId").GetGuid(), Is.EqualTo(userId));
                Assert.That(row.TryGetProperty("fieldName", out _), Is.True);
                Assert.That(row.TryGetProperty("oldValue", out _), Is.True);
                Assert.That(row.TryGetProperty("newValue", out _), Is.True);
                Assert.That(row.TryGetProperty("changedAt", out _), Is.True);
            });

            // 字段名过滤
            var byField = await GetJsonAsync($"/api/system/entity-change/page?entityId={userId}&fieldName=NickName&pageSize=10");
            Assert.That(byField.RootElement.GetProperty("data").GetProperty("total").GetInt64(), Is.GreaterThanOrEqualTo(1), "字段名过滤应命中");

            // 不存在的实体 Id
            var none = await GetJsonAsync($"/api/system/entity-change/page?entityId={Guid.NewGuid()}&pageSize=10");
            Assert.That(none.RootElement.GetProperty("data").GetProperty("total").GetInt64(), Is.EqualTo(0));
        }
        finally
        {
            await SendAsync(HttpMethod.Delete, $"/api/system/user/{userId}/{await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId)}", null);
        }
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

    /// <summary>以 admin 创建用户（唯一用户名），返回用户 Id。</summary>
    private async Task<Guid> CreateUserAsync(string userName)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/system/user", new
        {
            userName,
            nickName = "变更审计测试用户",
            password = "Init@Change123",
            status = 1,
            roleIds = Array.Empty<Guid>()
        });
        var body = await ReadBodyAsync(response);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"创建用户应成功：{body.RootElement.GetProperty("message").GetString()}");
        return await ApiTestSupport.GetEntityIdAsync<SysUser>(_db, it => it.UserName == userName);
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

    /// <summary>发送请求并把响应解析为 JsonDocument（断言成功状态）。</summary>
    private static async Task<JsonDocument> SendForJsonAsync(HttpMethod method, string url, object payload)
    {
        var response = await SendAsync(method, url, payload);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True, $"请求应成功：{method} {url}，响应：{content}");
        return JsonDocument.Parse(content);
    }

    /// <summary>GET 并解析响应（断言成功状态，携带 admin 令牌）。</summary>
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
