
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using Application.Contracts.Auth;
using Domain.Services.Security;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests;

/// <summary>
/// refreshToken HttpOnly Cookie 可选模式回归：默认关闭时契约不变；开启后
/// 登录 / 刷新下发安全 Cookie，刷新与登出可接受「仅 Cookie、不带请求体令牌」，
/// 登出删除 Cookie。响应体仍返回 refreshToken，前端可平滑迁移。
/// </summary>
[TestFixture]
public class RefreshTokenCookieTests
{
    private const string CookieName = "super-admin-refresh";
    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ICache _cache = null!;

    /// <summary>启动开启 Cookie 模式的测试宿主。</summary>
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
                        ["RateLimit:Enabled"] = "false",
                        ["Jwt:RefreshTokenCookieEnabled"] = "true",
                        ["Jwt:RefreshTokenCookieName"] = CookieName
                    });
                });
            });

        _httpClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _cache = _factory.Services.GetRequiredService<ICache>();
    }

    /// <summary>释放宿主资源。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
        _factory.Dispose();
    }

    /// <summary>登录响应须下发 HttpOnly / Secure / SameSite=Strict / Path=/api/auth 的 Cookie。</summary>
    [Test]
    public async Task Login_WritesSecureRefreshTokenCookie()
    {
        var (response, _) = await LoginAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var setCookie = string.Join(";", response.Headers.GetValues("Set-Cookie"));
        Assert.Multiple(() =>
        {
            Assert.That(setCookie, Does.Contain(CookieName + "="), "应下发 refreshToken Cookie");
            Assert.That(setCookie, Does.Contain("httponly"), "Cookie 必须 HttpOnly");
            Assert.That(setCookie, Does.Contain("secure"), "Cookie 必须 Secure");
            Assert.That(setCookie, Does.Contain("samesite=strict"), "Cookie 必须 SameSite=Strict");
            Assert.That(setCookie, Does.Contain("path=/api/auth"), "Cookie 路径应限定在认证端点");
        });
    }

    /// <summary>仅携带 Cookie（请求体不传令牌）即可完成刷新，且刷新会轮换 Cookie。</summary>
    [Test]
    public async Task Refresh_WithCookieOnly_SucceedsAndRotatesCookie()
    {
        var (_, tokens) = await LoginAsync();
        Assert.That(tokens, Is.Not.Null);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { refreshToken = string.Empty })
        };
        request.Headers.Add("Cookie", $"{CookieName}={tokens!.RefreshToken}");
        var response = await _httpClient.SendAsync(request);
        var body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "仅 Cookie 也应能刷新");
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(200));
            Assert.That(body.RootElement.GetProperty("data").GetProperty("refreshToken").GetString(),
                Is.Not.EqualTo(tokens.RefreshToken), "刷新应轮换令牌");
            Assert.That(string.Join(";", response.Headers.GetValues("Set-Cookie")),
                Does.Contain(CookieName + "="), "刷新应写入新 Cookie");
        });
    }

    /// <summary>登出须删除 Cookie，且可由 Cookie 定位要吊销的会话。</summary>
    [Test]
    public async Task Logout_DeletesRefreshTokenCookie()
    {
        var (_, tokens) = await LoginAsync();
        Assert.That(tokens, Is.Not.Null);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        request.Headers.Add("Cookie", $"{CookieName}={tokens.RefreshToken}");
        var response = await _httpClient.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var setCookie = string.Join(";", response.Headers.GetValues("Set-Cookie"));
        Assert.Multiple(() =>
        {
            Assert.That(setCookie, Does.Contain(CookieName + "="), "应下发删除 Cookie 的 Set-Cookie");
            Assert.That(setCookie.ToLowerInvariant(), Does.Contain("expires="),
                "删除 Cookie 须带过期属性");
        });
    }

    /// <summary>登录并返回令牌对（经真实验证码流程）。</summary>
    /// <returns>响应与令牌对。</returns>
    private async Task<(HttpResponseMessage Response, TokenPairDto? Tokens)> LoginAsync()
    {
        var captchaResponse = await _httpClient.GetAsync("/api/auth/captcha");
        captchaResponse.EnsureSuccessStatusCode();
        var captchaBody = await ReadBodyAsync(captchaResponse);
        var captchaId = captchaBody.RootElement.GetProperty("data").GetProperty("captchaId").GetString()!;
        var captchaCode = await _cache.GetAsync<string>(ICaptchaService.CacheKeyPrefix + captchaId);

        var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new
        {
            userName = AdminUserName,
            password = AdminPassword,
            captchaId,
            captchaCode
        });

        if (!response.IsSuccessStatusCode)
        {
            return (response, null);
        }

        var body = await ReadBodyAsync(response);
        var data = body.RootElement.GetProperty("data");
        return (response, new TokenPairDto
        {
            AccessToken = data.GetProperty("accessToken").GetString()!,
            RefreshToken = data.GetProperty("refreshToken").GetString()!,
            ExpiresIn = data.GetProperty("expiresIn").GetInt64()
        });
    }

    /// <summary>解析响应体为 JSON 文档。</summary>
    /// <param name="response">HTTP 响应。</param>
    /// <returns>JSON 文档。</returns>
    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
