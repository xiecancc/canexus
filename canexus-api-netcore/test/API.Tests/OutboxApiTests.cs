using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using Domain.Entities;
using Domain.Events;
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
/// 领域事件 Outbox（P1-A2）集成测试：
/// 验证领域事件在事务内持久化、事务提交后同步投递完成、投递失败可重试且不影响主流程。
/// <para>通过真实 HTTP 管道 + 真实 MySQL；后台 Outbox 投递 worker 在本套件启用（1 秒轮询）。</para>
/// </summary>
[TestFixture]
public class OutboxApiTests
{
    private const string UserName = "outbox_user";
    private const string Password = "Test123456";
    private const string NewPassword = "Test654321";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static ICache _cache = null!;
    private static IPasswordHasher _passwordHasher = null!;

    /// <summary>测试套件初始化：启动测试宿主（启用 Outbox worker，1 秒轮询、重试上限放宽）。</summary>
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
                        ["Outbox:Enabled"] = "true",
                        ["Outbox:PollIntervalSeconds"] = "1",
                        ["Outbox:MaxAttempts"] = "100" // 放宽重试上限，便于断言"仍处于可重试状态"
                    });
                });
            });

        _httpClient = _factory.CreateClient();
        _db = _factory.Services.GetRequiredService<ISqlSugarClient>();
        _cache = _factory.Services.GetRequiredService<ICache>();
        _passwordHasher = _factory.Services.GetRequiredService<IPasswordHasher>();
    }

    /// <summary>测试套件清理。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
        _db.Dispose();
        (_cache as IDisposable)?.Dispose();
        _factory.Dispose();
    }

    // —— P1-A2：Outbox ——

    /// <summary>核心断言：领域事件随业务事务写入 Outbox 表，事务提交后同步投递完成（ProcessedAt 已写、状态为已处理）。</summary>
    [Test]
    public async Task Outbox_EventPersistedAndDeliveredAfterCommit()
    {
        var token = await CreateUserAndLoginAsync();
        try
        {
            var response = await PostJsonAsync("/api/auth/change-password", new { oldPassword = Password, newPassword = NewPassword }, token);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "改密请求应成功（Outbox 不应影响主流程）");

            var row = await _db.Queryable<OutboxMessage>()
                .Where(message => message.EventType == typeof(UserPasswordResetEvent).FullName)
                .OrderBy(message => message.CreatedAt, OrderByType.Desc)
                .FirstAsync();
            Assert.Multiple(() =>
            {
                Assert.That(row, Is.Not.Null, "领域事件应持久化到 Outbox 表");
                Assert.That(row!.Status, Is.EqualTo(OutboxMessageStatus.Processed), "事务提交后应同步投递完成");
                Assert.That(row.ProcessedAt, Is.Not.Null, "投递完成应记录 ProcessedAt");
                // T-16：Outbox 载荷统一复用 JsonHelper.WebOptions（camelCase）
                Assert.That(row.Payload, Does.Contain("\"userId\""), "Outbox 载荷应包含可反序列化的事件内容");
            });
        }
        finally
        {
            await CleanupUserAsync();
            await _db.Deleteable<OutboxMessage>()
                .Where(message => message.EventType == typeof(UserPasswordResetEvent).FullName)
                .ExecuteCommandAsync();
        }
    }

    /// <summary>核心断言：投递失败（载荷无法反序列化）记录失败并保持待重试状态，且失败投递不影响正常业务与事件投递。</summary>
    [Test]
    public async Task Outbox_FailedDelivery_RetriesWithoutAffectingMainFlow()
    {
        // 直接插入一条无法反序列化的 Outbox 记录（EventType 指向不存在的类型）
        var corruptId = Guid.NewGuid();
        await _db.Insertable(new OutboxMessage
        {
            Id = corruptId,
            CreatedAt = DateTime.UtcNow,
            EventType = "No.Such.EventType",
            Payload = "{}",
            Status = OutboxMessageStatus.Pending
        }).ExecuteCommandAsync();
        try
        {
            // 后台 worker 应重试并记录失败（AttemptCount 增长、LastError 非空、仍为待处理）
            await WaitUntilAsync(async () =>
            {
                var row = await _db.Queryable<OutboxMessage>().Where(message => message.Id == corruptId).FirstAsync();
                return row is not null
                       && row.AttemptCount >= 1
                       && !string.IsNullOrEmpty(row.LastError)
                       && row.Status == OutboxMessageStatus.Pending;
            }, TimeSpan.FromSeconds(20), "投递失败应记录 AttemptCount/LastError 并保持待重试");

            // 失败重试不应影响主流程：正常业务照常成功、自身事件照常投递完成
            var token = await CreateUserAndLoginAsync();
            var response = await PostJsonAsync("/api/auth/change-password", new { oldPassword = Password, newPassword = NewPassword }, token);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "存在失败投递时正常业务请求仍应成功");

            var healthyRow = await _db.Queryable<OutboxMessage>()
                .Where(message => message.EventType == typeof(UserPasswordResetEvent).FullName)
                .OrderBy(message => message.CreatedAt, OrderByType.Desc)
                .FirstAsync();
            Assert.That(healthyRow?.Status, Is.EqualTo(OutboxMessageStatus.Processed), "正常事件的投递不应受失败记录影响");
        }
        finally
        {
            await CleanupUserAsync();
            await _db.Deleteable<OutboxMessage>()
                .Where(message => message.Id == corruptId || message.EventType == typeof(UserPasswordResetEvent).FullName)
                .ExecuteCommandAsync();
        }
    }

    // —— 测试辅助 ——

    /// <summary>幂等创建测试用户并登录，返回 accessToken。</summary>
    private async Task<string> CreateUserAndLoginAsync()
    {
        var existing = await _db.Queryable<SysUser>().Where(user => user.UserName == UserName).FirstAsync();
        if (existing is null)
        {
            await _db.Insertable(new SysUser
            {
                UserName = UserName,
                NickName = "Outbox 测试用户",
                PasswordHash = _passwordHasher.Hash(Password),
                Status = CommonStatus.Enabled
            }).ExecuteCommandAsync();
        }
        else
        {
            existing.PasswordHash = _passwordHasher.Hash(Password);
            existing.Status = CommonStatus.Enabled;
            await _db.Updateable(existing).ExecuteCommandAsync();
        }

        ClearLoginFailState(UserName);
        return await LoginAndGetAccessTokenAsync(UserName, Password);
    }

    /// <summary>清理测试用户及其登录日志 / 刷新令牌。</summary>
    private async Task CleanupUserAsync()
    {
        await _db.Deleteable<SysLoginLog>().Where(log => log.UserName == UserName).ExecuteCommandAsync();
        await _db.Deleteable<SysUser>().Where(user => user.UserName == UserName).ExecuteCommandAsync();
    }

    /// <summary>轮询等待条件成立，超时失败。</summary>
    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string message)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        Assert.Fail($"{message}（等待超时 {timeout.TotalSeconds:0} 秒）");
    }

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
        var response = await PostJsonAsync("/api/auth/login", new { userName, password, captchaId, captchaCode });
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            Assert.Fail($"登录应成功：{userName}，HTTP {(int)response.StatusCode}，响应：{content}");
        }

        var body = await ReadBodyAsync(response);
        return body.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    /// <summary>POST JSON 请求（可携带令牌）。</summary>
    private async Task<HttpResponseMessage> PostJsonAsync(string url, object payload, string? accessToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await _httpClient.SendAsync(request);
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
