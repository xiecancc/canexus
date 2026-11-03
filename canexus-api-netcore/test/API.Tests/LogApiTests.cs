
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
/// 操作日志与登录日志（B8）集成测试：
/// 走真实 HTTP 管道 + 真实 MySQL（默认库 super_admin_test，SUPER_ADMIN_TEST_CONN 可覆盖），
/// 重点验证 [OperLog] 写入与分页查询、敏感字段脱敏、失败请求记录错误、清空与登录日志查询。
/// </summary>
[TestFixture]
public class LogApiTests
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

        // 测试隔离：清空历史登录日志（共享测试库可能残留其他夹具写入的失败登录记录），保证分页断言从已知状态开始
        await _db.Deleteable<SysLoginLog>().ExecuteCommandAsync();

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

    // —— 操作日志写入与查询 ——

    /// <summary>验证创建字典类型时操作日志被写入且可分页查询。</summary>
    [Test]
    public async Task OperLog_CreateDictType_WrittenAndQueryable()
    {
        var dictType = "b8_log_" + Guid.NewGuid().ToString("N")[..10];
        await SendForJsonAsync(HttpMethod.Post, "/api/system/dict-type", new
        {
            dictName = "B8 日志字典",
            dictType
        });
        var typeId = await ApiTestSupport.GetEntityIdAsync<SysDictType>(_db, it => it.DictType == dictType);

        var page = await GetJsonAsync("/api/system/oper-log/page?title=字典类型&operUserName=admin&pageSize=100");
        var items = page.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(i => (i.GetProperty("operParam").GetString() ?? "").Contains(dictType))
            .ToList();

        Assert.That(items, Is.Not.Empty, "带 [OperLog] 的写接口应产生操作日志");
        var log = items[^1];
        Assert.Multiple(() =>
        {
            Assert.That(page.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(200));
            Assert.That(log.GetProperty("title").GetString(), Is.EqualTo("字典类型"));
            Assert.That(log.GetProperty("businessType").GetInt32(), Is.EqualTo(1), "新增业务类型应为 1");
            Assert.That(log.GetProperty("status").GetInt32(), Is.EqualTo(1), "成功请求应记录为成功");
            Assert.That(log.GetProperty("method").GetString(), Does.Contain("SysDictTypeController.Create"));
            Assert.That(log.GetProperty("requestMethod").GetString(), Is.EqualTo("POST"));
            Assert.That(log.GetProperty("requestUrl").GetString(), Does.Contain("/api/system/dict-type"));
            Assert.That(log.GetProperty("operUserName").GetString(), Is.EqualTo(AdminUserName));
            Assert.That(log.GetProperty("costMs").GetInt64(), Is.GreaterThanOrEqualTo(0), "应记录耗时");
            Assert.That(log.TryGetProperty("operTime", out _), Is.True, "应记录操作时间");
        });

        await SendAsync(HttpMethod.Delete, $"/api/system/dict-type/{typeId}/{await ApiTestSupport.GetVersionAsync<SysDictType>(_db, typeId)}", null);
    }

    /// <summary>验证操作日志中敏感字段（密码等）被脱敏为六颗星且不出现明文。</summary>
    [Test]
    public async Task OperLog_SensitiveFields_MaskedAsSixStars()
    {
        var userName = "b8" + Guid.NewGuid().ToString("N")[..12];
        var userId = await CreateUserAsync(userName: userName, password: "Init@Mask123");

        // 重置密码（明文出现在请求体中，日志必须脱敏）
        await SendAsync(HttpMethod.Put, $"/api/system/user/{userId}/{await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId)}/password", new
        {
            password = "New@Secret456"
        });

        var page = await GetJsonAsync("/api/system/oper-log/page?title=用户管理&operUserName=admin&pageSize=100");
        var items = page.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();

        // 重置密码日志：密码字段必须脱敏为 ****** 且不出现明文
        var resetParam = items
            .Where(i => (i.GetProperty("requestUrl").GetString() ?? "").Contains("/password"))
            .Select(i => i.GetProperty("operParam").GetString() ?? string.Empty)
            .LastOrDefault();
        Assert.That(resetParam, Is.Not.Null, "重置密码应产生操作日志");
        Assert.Multiple(() =>
        {
            Assert.That(resetParam, Does.Contain("******"), "密码字段应被脱敏为 ******");
            Assert.That(resetParam, Does.Not.Contain("New@Secret456"), "新密码明文不得出现在日志中");
        });

        // 创建用户时携带的初始密码同样脱敏
        var createParam = items
            .Where(i => (i.GetProperty("method").GetString() ?? "").Contains("SysUserController.Create")
                        && (i.GetProperty("operParam").GetString() ?? "").Contains(userName))
            .Select(i => i.GetProperty("operParam").GetString() ?? string.Empty)
            .LastOrDefault();
        Assert.That(createParam, Is.Not.Null, "创建用户应产生操作日志");
        Assert.Multiple(() =>
        {
            Assert.That(createParam, Does.Contain("******"));
            Assert.That(createParam, Does.Not.Contain("Init@Mask123"), "初始密码明文不得出现在日志中");
        });

        await SendAsync(HttpMethod.Delete, $"/api/system/user/{userId}/{await ApiTestSupport.GetVersionAsync<SysUser>(_db, userId)}", null);
    }

    /// <summary>验证失败请求记录错误信息且状态记为失败（0）。</summary>
    [Test]
    public async Task OperLog_FailedRequest_RecordsErrorAndStatus0()
    {
        var dictType = "b8_fail_" + Guid.NewGuid().ToString("N")[..10];
        await SendForJsonAsync(HttpMethod.Post, "/api/system/dict-type", new
        {
            dictName = "B8 失败字典",
            dictType
        });
        var typeId = await ApiTestSupport.GetEntityIdAsync<SysDictType>(_db, it => it.DictType == dictType);

        // 重复创建（业务异常路径）→ 400602
        var duplicate = await SendAsync(HttpMethod.Post, "/api/system/dict-type", new
        {
            dictName = "B8 失败字典",
            dictType
        });
        Assert.That(duplicate.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var page = await GetJsonAsync("/api/system/oper-log/page?title=字典类型&status=0&pageSize=100");
        var failed = page.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(i => (i.GetProperty("operParam").GetString() ?? "").Contains(dictType))
            .ToList();

        Assert.That(failed, Is.Not.Empty, "失败的写请求也应记录操作日志");
        var log = failed[^1];
        Assert.Multiple(() =>
        {
            Assert.That(log.GetProperty("status").GetInt32(), Is.EqualTo(0), "失败请求应记录为失败");
            Assert.That(log.GetProperty("errorMsg").GetString(), Does.Contain("已存在"), "应记录业务异常消息");
            Assert.That(log.TryGetProperty("costMs", out _), Is.True);
        });

        await SendAsync(HttpMethod.Delete, $"/api/system/dict-type/{typeId}/{await ApiTestSupport.GetVersionAsync<SysDictType>(_db, typeId)}", null);
    }

    /// <summary>验证清空操作日志接口移除全部日志记录。</summary>
    [Test]
    public async Task OperLog_Clean_RemovesAllLogs()
    {
        var clean = await SendAsync(HttpMethod.Post, "/api/system/oper-log/clean", null);
        Assert.That(clean.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var page = await GetJsonAsync("/api/system/oper-log/page?pageIndex=1&pageSize=10");
        var data = page.RootElement.GetProperty("data");
        Assert.Multiple(() =>
        {
            Assert.That(data.GetProperty("total").GetInt64(), Is.EqualTo(0), "清空后操作日志应为空（清空接口自身不记录日志）");
            Assert.That(data.GetProperty("items").GetArrayLength(), Is.EqualTo(0));
        });
    }

    // —— 登录日志 ——

    /// <summary>验证登录日志分页查询、时间范围过滤及清空功能。</summary>
    [Test]
    public async Task LoginLog_PageQuery_AndClean()
    {
        // Setup 阶段 admin 已登录 → 登录日志应存在
        var page = await GetJsonAsync("/api/system/login-log/page?userName=admin&pageSize=10");
        var data = page.RootElement.GetProperty("data");
        Assert.That(data.GetProperty("total").GetInt64(), Is.GreaterThanOrEqualTo(1), "登录后应有登录日志");

        var row = data.GetProperty("items")[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("userName").GetString(), Is.EqualTo(AdminUserName));
            Assert.That(row.GetProperty("status").GetInt32(), Is.EqualTo(1));
            Assert.That(row.TryGetProperty("msg", out _), Is.True);
            Assert.That(row.TryGetProperty("loginTime", out _), Is.True);
            Assert.That(row.TryGetProperty("ip", out _), Is.True);
        });

        // 时间范围过滤（今日，yyyy-MM-dd）仍应命中
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var ranged = await GetJsonAsync($"/api/system/login-log/page?userName=admin&beginTime={today}&endTime={today}&pageSize=10");
        Assert.That(ranged.RootElement.GetProperty("data").GetProperty("total").GetInt64(),
            Is.GreaterThanOrEqualTo(1), "yyyy-MM-dd 时间范围过滤应含当日");

        // 清空后为空
        var clean = await SendAsync(HttpMethod.Post, "/api/system/login-log/clean", null);
        Assert.That(clean.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var afterClean = await GetJsonAsync("/api/system/login-log/page?pageIndex=1&pageSize=10");
        Assert.That(afterClean.RootElement.GetProperty("data").GetProperty("total").GetInt64(), Is.EqualTo(0));
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
    private async Task<Guid> CreateUserAsync(string userName, string password)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/system/user", new
        {
            userName,
            nickName = "B8 日志测试用户",
            password,
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
