using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using API;
using Domain.Repositories;
using Domain.Services.Security;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace API.Tests;

/// <summary>
/// 登录日志写入韧性集成测试（T-12）：把 ILoginLogRepository 换成始终抛错的桩，
/// 验证日志库不可用时认证链路仍返回令牌，不被日志异常顶替。
/// </summary>
[TestFixture]
public class LoginLogResilienceApiTests
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ICache _cache = null!;

    /// <summary>启动注入故障仓储桩的测试宿主。</summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
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
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ILoginLogRepository>();
                    services.AddSingleton(DispatchProxy.Create<ILoginLogRepository, ThrowingLoginLogRepository>());
                });
            });

        _httpClient = _factory.CreateClient();
        _cache = _factory.Services.GetRequiredService<ICache>();
    }

    /// <summary>测试套件清理：释放宿主与资源。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
        (_cache as IDisposable)?.Dispose();
        _factory.Dispose();
    }

    /// <summary>登录日志写入失败时仍应返回 200 与访问令牌。</summary>
    [Test]
    public async Task Login_WhenLoginLogRepositoryThrows_StillReturnsToken()
    {
        var (captchaId, captchaCode) = await GetCaptchaAsync();
        var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new
        {
            userName = AdminUserName,
            password = AdminPassword,
            captchaId,
            captchaCode
        });
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"认证不应被日志异常阻断，响应：{body}");
            Assert.That(document.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(200));
            Assert.That(document.RootElement.GetProperty("data").GetProperty("accessToken").GetString(),
                Is.Not.Null.And.Not.Empty);
        });
    }

    /// <summary>获取验证码并从缓存读取明文。</summary>
    private async Task<(string CaptchaId, string CaptchaCode)> GetCaptchaAsync()
    {
        var response = await _httpClient.GetAsync("/api/auth/captcha");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var captchaId = document.RootElement.GetProperty("data").GetProperty("captchaId").GetString()!;
        var code = await _cache.GetAsync<string>(ICaptchaService.CacheKeyPrefix + captchaId);
        Assert.That(code, Is.Not.Null, "验证码应已写入缓存");
        return (captchaId, code!);
    }
}

/// <summary>始终抛错的登录日志仓储桩：模拟日志库不可用。</summary>
public class ThrowingLoginLogRepository : DispatchProxy
{
    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new InvalidOperationException("登录日志库不可用（测试桩）");
}
