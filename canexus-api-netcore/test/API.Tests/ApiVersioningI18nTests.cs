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
/// API 版本化与请求本地化集成测试：
/// 验证响应头 api-supported-versions 声明、未指定版本回退默认（全量既有请求兼容），
/// 以及业务异常消息按 Accept-Language 协商翻译（zh-CN 默认 / en-US 英文）。
/// </summary>
[TestFixture]
public class ApiVersioningI18nTests
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
        _adminToken = await LoginAsync(AdminUserName, AdminPassword);
    }

    /// <summary>测试套件清理：释放宿主与资源，并清理探针用户。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _db.Ado.ExecuteCommandAsync("DELETE FROM sys_user WHERE UserName LIKE 'i18np%'").GetAwaiter().GetResult();
        _httpClient.Dispose();
        _db.Dispose();
        (_cache as IDisposable)?.Dispose();
        _factory.Dispose();
    }

    // —— API 版本化 ——

    /// <summary>验证未指定版本（默认落法）请求正常响应，且响应头声明支持版本集。</summary>
    [Test]
    public async Task Request_WithoutVersion_ReturnsSupportedVersionsHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/system/user/page?pageSize=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        using var response = await _httpClient.SendAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "未指定版本应回退默认 v1.0 正常响应");
            Assert.That(response.Headers.Contains("api-supported-versions"), Is.True, "响应头应声明支持版本集");
            var header = string.Join(",", response.Headers.GetValues("api-supported-versions"));
            Assert.That(header, Does.Contain("1.0"));
        });
    }

    /// <summary>验证查询串显式请求 v1.0 与请求头方式均正常路由。</summary>
    [Test]
    public async Task Request_WithExplicitVersion_RoutesNormally()
    {
        using var byQuery = new HttpRequestMessage(HttpMethod.Get, "/api/system/user/page?pageSize=1&api-version=1.0");
        byQuery.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        using var responseByQuery = await _httpClient.SendAsync(byQuery);
        Assert.That(responseByQuery.StatusCode, Is.EqualTo(HttpStatusCode.OK), "查询串显式 v1.0 应正常路由");

        using var byHeader = new HttpRequestMessage(HttpMethod.Get, "/api/system/user/page?pageSize=1");
        byHeader.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        byHeader.Headers.Add("X-Api-Version", "1.0");
        using var responseByHeader = await _httpClient.SendAsync(byHeader);
        Assert.That(responseByHeader.StatusCode, Is.EqualTo(HttpStatusCode.OK), "请求头显式 v1.0 应正常路由");
    }

    // —— 请求本地化 ——

    /// <summary>验证默认文化（无 Accept-Language）业务异常消息为中文，Accept-Language: en-US 为英文。</summary>
    [Test]
    public async Task BusinessException_Message_LocalizedByAcceptLanguage()
    {
        // 造一个非 admin 用户（admin 受保护拒绝编辑），用随机版本触发乐观锁并发冲突（40005）
        var userName = $"i18np" + Guid.NewGuid().ToString("N")[..8];
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/system/user");
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        createRequest.Content = JsonContent.Create(new
        {
            userName,
            nickName = "本地化探针",
            password = "Probe@2026",
            deptId = (await _db.Queryable<SysDept>().FirstAsync()).Id,
            email = "probe@example.com",
            phone = "13900001234",
            status = 1,
            roleIds = Array.Empty<Guid>()
        });
        var createResponse = await _httpClient.SendAsync(createRequest);
        Assert.That(createResponse.IsSuccessStatusCode, Is.True,
            $"探针用户应创建成功：{await createResponse.Content.ReadAsStringAsync()}");
        var user = await _db.Queryable<SysUser>().Where(u => u.UserName == userName).FirstAsync();
        Assert.That(user, Is.Not.Null, "探针用户应创建成功");

        // 默认文化（中文）
        using var zhRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/system/user/{user.Id}/{Guid.NewGuid()}");
        zhRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        zhRequest.Content = JsonContent.Create(new { nickName = "改名", status = 1, roleIds = Array.Empty<Guid>() });
        using var zhResponse = await _httpClient.SendAsync(zhRequest);
        var zhBody = await zhResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Multiple(() =>
        {
            Assert.That(zhBody.GetProperty("code").GetInt32(), Is.EqualTo(ErrorCodes.CommonConcurrencyConflict));
            Assert.That(zhBody.GetProperty("message").GetString(), Does.Contain("数据已被他人修改"), "默认文化应为中文消息");
        });

        // 英文文化
        using var enRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/system/user/{user.Id}/{Guid.NewGuid()}");
        enRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        enRequest.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
        enRequest.Content = JsonContent.Create(new { nickName = "改名", status = 1, roleIds = Array.Empty<Guid>() });
        using var enResponse = await _httpClient.SendAsync(enRequest);
        var enBody = await enResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Multiple(() =>
        {
            Assert.That(enBody.GetProperty("code").GetInt32(), Is.EqualTo(ErrorCodes.CommonConcurrencyConflict));
            Assert.That(enBody.GetProperty("message").GetString(), Does.Contain("version conflict"), "en-US 应为英文消息");
        });
    }

    // —— 公共辅助 ——

    /// <summary>获取图形验证码（经缓存读取真实验证码内容）。</summary>
    /// <returns>验证码 Id 与验证码内容。</returns>
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

    /// <summary>登录获取访问令牌（走验证码流程）。</summary>
    /// <param name="userName">用户名。</param>
    /// <param name="password">密码。</param>
    /// <returns>accessToken。</returns>
    private async Task<string> LoginAsync(string userName, string password)
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

    /// <summary>解析响应正文为 JSON 文档。</summary>
    /// <param name="response">HTTP 响应。</param>
    /// <returns>JSON 文档。</returns>
    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
