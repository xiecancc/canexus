using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using API.Middlewares;
using Domain.Services.Security;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests;
/// <summary>
/// 错误响应契约回归（SEC-05 复核）：校验权限过滤器、InvalidModelStateResponseFactory、
/// 全局异常处理三条出口的响应体结构与 X-Trace-Id 响应头是否一致。
/// <para>TraceIdMiddleware 已为全部响应写入 X-Trace-Id，本套件用于实测确认是否存在真实缺口，
/// 避免为「统一出口」这一形式目标做无收益重构。</para>
/// </summary>
[TestFixture]
public class ErrorResponseContractTests
{
    private const string TraceIdHeader = "X-Trace-Id";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ICache _cache = null!;
    private static string _adminToken = null!;

    /// <summary>启动测试宿主并以平台 admin 登录。</summary>
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
        _cache = _factory.Services.GetRequiredService<ICache>();
        _adminToken = await LoginAsync();
    }

    /// <summary>释放宿主资源。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
        (_cache as IDisposable)?.Dispose();
        _factory.Dispose();
    }

    /// <summary>权限过滤器 401 出口。</summary>
    [Test]
    public async Task PermissionFilter_Unauthorized_HasTraceIdAndUnifiedBody()
    {
        var response = await _httpClient.GetAsync("/api/system/user/page");
        await AssertUnifiedAsync(response, HttpStatusCode.Unauthorized);
    }

    /// <summary>模型绑定校验 400 出口（InvalidModelStateResponseFactory）。</summary>
    [Test]
    public async Task InvalidModelState_HasTraceIdAndUnifiedBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { })
        };
        var response = await _httpClient.SendAsync(request);
        await AssertUnifiedAsync(response, HttpStatusCode.BadRequest);
    }

    /// <summary>业务异常出口（全局异常处理中间件 → UnifiedResponseWriter）。</summary>
    [Test]
    public async Task BusinessException_HasTraceIdAndUnifiedBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Put,
            $"/api/system/user/{Guid.NewGuid()}/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new { userName = "x", nickName = "x" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        var response = await _httpClient.SendAsync(request);
        await AssertUnifiedAsync(response);
    }

    /// <summary>断言统一响应体结构与 TraceId 响应头，并输出 Content-Type 供差异核对。</summary>
    private static async Task AssertUnifiedAsync(HttpResponseMessage response, HttpStatusCode? expected = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        TestContext.Out.WriteLine($"[contract] status={(int)response.StatusCode} contentType={response.Content.Headers.ContentType} body={body}");

        if (expected is not null)
        {
            Assert.That(response.StatusCode, Is.EqualTo(expected.Value));
        }

        Assert.That(response.Headers.Contains(TraceIdHeader), Is.True,
            $"错误响应应带 {TraceIdHeader} 响应头：{body}");
        Assert.That(response.Headers.GetValues(TraceIdHeader).First(), Is.Not.Empty,
            $"{TraceIdHeader} 不应为空：{body}");

        using var json = JsonDocument.Parse(body);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.TryGetProperty("code", out _), Is.True, "错误响应应包含 code");
            Assert.That(json.RootElement.TryGetProperty("message", out _), Is.True, "错误响应应包含 message");
        });
    }

    /// <summary>平台 admin 登录，返回访问令牌。</summary>
    private async Task<string> LoginAsync()
    {
        var captchaResponse = await _httpClient.GetAsync("/api/auth/captcha");
        captchaResponse.EnsureSuccessStatusCode();
        using var captchaBody = JsonDocument.Parse(await captchaResponse.Content.ReadAsStringAsync());
        var captchaId = captchaBody.RootElement.GetProperty("data").GetProperty("captchaId").GetString()!;
        var captchaCode = await _cache.GetAsync<string>(ICaptchaService.CacheKeyPrefix + captchaId);
        Assert.That(captchaCode, Is.Not.Null, "验证码应已写入缓存");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                userName = "admin",
                password = "admin123",
                captchaId,
                captchaCode
            })
        };
        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True, $"登录应成功：{content}");
        using var body = JsonDocument.Parse(content);
        return body.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }
}
