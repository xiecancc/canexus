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
/// 默认审计（AuditLogBehavior，P0）集成测试：
/// 走真实 HTTP 管道 + 真实 MySQL，验证未标注 [OperLog] 的写命令也写入操作日志
/// （命令元数据 + TraceId + UserId + 敏感字段脱敏），且 [OperLog] 端点不被重复记录。
/// </summary>
[TestFixture]
public class OperLogDefaultAuditApiTests
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static ICache _cache = null!;
    private static string _adminToken = null!;
    private static Guid _adminUserId = Guid.Empty;

    /// <summary>测试套件初始化：启动测试宿主并准备种子数据。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = ApiTestSupport.TestConnectionString;
        var apiContentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "API"));
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
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
        _adminUserId = await ApiTestSupport.GetEntityIdAsync<SysUser>(_db, it => it.UserName == AdminUserName);
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

    /// <summary>未标注 [OperLog] 的写命令（登出）应被默认审计记录：命令元数据 + TraceId + UserId + 业务类型 None。</summary>
    [Test]
    public async Task DefaultAudit_UndeclaredCommand_WrittenWithTraceIdAndUserId()
    {
        await SendAsync(HttpMethod.Post, "/api/auth/logout", new { refreshToken = "stub-refresh-token-value" });

        // 登出后旧令牌可能已吊销：重新登录获取新令牌再查询
        _adminToken = await LoginAndGetAccessTokenAsync(AdminUserName, AdminPassword);
        var page = await GetJsonAsync("/api/system/oper-log/page?pageSize=100");
        var items = page.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("requestMethod").GetString() == "COMMAND"
                        && (i.GetProperty("method").GetString() ?? "").Contains("AuthLogoutCommand"))
            .ToList();

        Assert.That(items, Is.Not.Empty, "未标注 [OperLog] 的写命令（登出）应被默认审计记录");
        var log = items[^1];
        Assert.Multiple(() =>
        {
            Assert.That(log.GetProperty("title").GetString(), Is.EqualTo("AuthLogoutCommand"), "默认审计以命令类型名为模块");
            Assert.That(log.GetProperty("businessType").GetInt32(), Is.EqualTo((int)OperBusinessType.None), "默认审计业务类型应为 None(0)");
            Assert.That(log.GetProperty("status").GetInt32(), Is.EqualTo(1), "成功命令应记录为成功");
            Assert.That(log.GetProperty("method").GetString(), Does.Contain("AuthLogoutCommand"));
            Assert.That(log.GetProperty("requestMethod").GetString(), Is.EqualTo("COMMAND"), "命令通道请求方式应标识为 COMMAND");
            Assert.That(log.GetProperty("operUserName").GetString(), Is.EqualTo(AdminUserName));
            Assert.That(log.GetProperty("userId").GetString(), Is.Not.Empty, "应记录操作人用户 Id");
            Assert.That(log.GetProperty("traceId").GetString(), Is.Not.Empty, "应记录请求链路 TraceId");
            Assert.That(log.GetProperty("costMs").GetInt64(), Is.GreaterThanOrEqualTo(0), "应记录耗时");
        });
    }

    /// <summary>默认审计载荷脱敏：登出请求中的 refreshToken 必须脱敏为六颗星，不得落明文。</summary>
    [Test]
    public async Task DefaultAudit_RefreshToken_Masked()
    {
        var tokenValue = "plain-" + Guid.NewGuid().ToString("N");
        await SendAsync(HttpMethod.Post, "/api/auth/logout", new { refreshToken = tokenValue });
        _adminToken = await LoginAndGetAccessTokenAsync(AdminUserName, AdminPassword);

        var page = await GetJsonAsync("/api/system/oper-log/page?pageSize=100");
        var param = page.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("requestMethod").GetString() == "COMMAND"
                        && (i.GetProperty("method").GetString() ?? "").Contains("AuthLogoutCommand"))
            .Select(i => i.GetProperty("operParam").GetString() ?? string.Empty)
            .LastOrDefault();

        Assert.That(param, Is.Not.Null, "登出命令应产生默认审计记录");
        Assert.Multiple(() =>
        {
            Assert.That(param, Does.Contain("******"), "refreshToken 应被脱敏为 ******");
            Assert.That(param, Does.Not.Contain(tokenValue), "refreshToken 明文不得出现在审计记录中");
        });
    }

    /// <summary>标注 [OperLog] 的端点只产生过滤器记录（COMMAND 记录不出现），避免双份审计。</summary>
    [Test]
    public async Task DefaultAudit_OperLogEndpoint_NoDuplicate()
    {
        var dictType = "audit_dedupe_" + Guid.NewGuid().ToString("N")[..8];
        await SendForJsonAsync(HttpMethod.Post, "/api/system/dict-type", new
        {
            dictName = "默认审计去重字典",
            dictType
        });
        var typeId = await ApiTestSupport.GetEntityIdAsync<SysDictType>(_db, it => it.DictType == dictType);

        var page = await GetJsonAsync("/api/system/oper-log/page?pageSize=100");
        var records = page.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(i => (i.GetProperty("operParam").GetString() ?? "").Contains(dictType))
            .ToList();

        Assert.That(records, Is.Not.Empty, "带 [OperLog] 的端点应有过滤器记录");
        Assert.Multiple(() =>
        {
            Assert.That(records, Has.All.Matches<JsonElement>(i =>
                    i.GetProperty("requestMethod").GetString() == "POST"),
                "[OperLog] 端点应仅由过滤器记录（requestMethod=POST），不得出现 COMMAND 重复记录");
            Assert.That(records.Count(i => i.GetProperty("title").GetString() == "字典类型"), Is.EqualTo(1),
                "同一操作应恰好一条过滤器审计记录");
        });

        await SendAsync(HttpMethod.Delete, $"/api/system/dict-type/{typeId}/{await ApiTestSupport.GetVersionAsync<SysDictType>(_db, typeId)}", null);
    }

    // —— 测试辅助 ——

    /// <summary>调用验证码接口并从缓存读取明文验证码。</summary>
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

    /// <summary>发送 JSON 请求（携带 admin 令牌）。</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? payload)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = payload is null ? null : JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        return await _httpClient.SendAsync(request);
    }

    /// <summary>发送请求并把响应解析为 JsonDocument（断言成功状态）。</summary>
    private async Task<JsonDocument> SendForJsonAsync(HttpMethod method, string url, object payload)
    {
        var response = await SendAsync(method, url, payload);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True, $"请求应成功：{method} {url}，响应：{content}");
        return JsonDocument.Parse(content);
    }

    /// <summary>GET 并解析响应（断言成功状态，携带 admin 令牌）。</summary>
    private async Task<JsonDocument> GetJsonAsync(string url)
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
