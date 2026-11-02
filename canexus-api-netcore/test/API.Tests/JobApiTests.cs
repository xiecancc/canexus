
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using Domain.Entities;
using Domain.Services.Security;
using Infrastructure.Scheduling;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace API.Tests;
/// <summary>
/// 定时任务（B10）集成测试：
/// 走真实 HTTP 管道 + 真实 MySQL（默认库 super_admin_test，SUPER_ADMIN_TEST_CONN 可覆盖）+ 真实 Quartz 调度器，
/// 重点验证任务 CRUD 与调度同步、Cron / 任务类型校验、立即执行产生执行日志（包装任务写入）与删除任务。
/// </summary>
[TestFixture]
public class JobApiTests
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

    [Test]
    public async Task Job_CreatePaused_RunOnce_ProducesLog_ChangeStatus_RunAgain_Delete()
    {
        // 1. 创建暂停任务（status=0 不入调度）
        var jobName = NewJobName();
        var createBody = await SendForJsonAsync(HttpMethod.Post, "/api/monitor/job", new
        {
            jobName,
            jobGroup = "DEFAULT",
            cronExpression = "0/1 * * * * ?",
            jobType = NoOpJob.TypeFullName,
            status = 0,
            concurrent = false,
            description = "B10 集成测试"
        });
        var jobId = await ApiTestSupport.GetEntityIdAsync<SysJob>(_db, it => it.JobName == jobName);

        // 分页字段校验
        var page = await GetJsonAsync($"/api/monitor/job/page?keyword={jobName}");
        var row = page.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("id").GetGuid(), Is.EqualTo(jobId));
            Assert.That(row.GetProperty("jobName").GetString(), Is.EqualTo(jobName));
            Assert.That(row.GetProperty("jobGroup").GetString(), Is.EqualTo("DEFAULT"));
            Assert.That(row.GetProperty("cronExpression").GetString(), Is.EqualTo("0/1 * * * * ?"));
            Assert.That(row.GetProperty("jobType").GetString(), Is.EqualTo(NoOpJob.TypeFullName));
            Assert.That(row.GetProperty("status").GetInt32(), Is.EqualTo(0));
            Assert.That(row.GetProperty("concurrent").GetBoolean(), Is.False);
            Assert.That(row.TryGetProperty("createdAt", out _), Is.True, "列表应包含 createdAt");
        });

        // 2. 暂停任务立即执行一次 → 轮询执行日志出现记录（超时 15s）
        await SendForJsonAsync(HttpMethod.Put, $"/api/monitor/job/{jobId}/run", null!);
        var firstLogs = await WaitForJobLogsAsync(jobId, minimumCount: 1, timeoutSeconds: 15);
        var firstLog = firstLogs[0];
        Assert.Multiple(() =>
        {
            Assert.That(firstLog.GetProperty("jobId").GetGuid(), Is.EqualTo(jobId));
            Assert.That(firstLog.GetProperty("jobName").GetString(), Is.EqualTo(jobName));
            Assert.That(firstLog.GetProperty("status").GetInt32(), Is.EqualTo(1), "NoOpJob 执行应成功");
            Assert.That(firstLog.GetProperty("triggerTime").GetDateTime(), Is.Not.EqualTo(default(DateTime)));
        });

        // 3. 修改状态为运行（1）→ 入调度（乐观锁：先回读版本号）
        var statusVersion = await ApiTestSupport.GetVersionAsync<SysJob>(_db, jobId);
        await SendForJsonAsync(HttpMethod.Put, $"/api/monitor/job/{jobId}/{statusVersion}/status", new { status = 1 });
        var afterChange = (await GetJsonAsync($"/api/monitor/job/page?keyword={jobName}"))
            .RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.That(afterChange.GetProperty("status").GetInt32(), Is.EqualTo(1), "启用后状态应为运行");

        // 4. 再立即执行一次 → 又一条日志
        await SendForJsonAsync(HttpMethod.Put, $"/api/monitor/job/{jobId}/run", null!);
        var secondLogs = await WaitForJobLogsAsync(jobId, minimumCount: 2, timeoutSeconds: 15);
        Assert.That(secondLogs[0].GetProperty("id").GetGuid(), Is.Not.EqualTo(secondLogs[1].GetProperty("id").GetGuid()),
            "两次执行应产生两条不同日志");
        Assert.That(secondLogs[0].GetProperty("triggerTime").GetDateTime(),
            Is.GreaterThanOrEqualTo(secondLogs[1].GetProperty("triggerTime").GetDateTime()),
            "日志应按触发时间倒序排列");

        // 5. 删除任务（同时移出调度）→ 分页不再返回
        var deleted = await SendAsync(HttpMethod.Delete, $"/api/monitor/job/{jobId}/{await ApiTestSupport.GetVersionAsync<SysJob>(_db, jobId)}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.OK), "删除任务应成功");
        var afterDelete = await GetJsonAsync($"/api/monitor/job/page?keyword={jobName}");
        Assert.That(afterDelete.RootElement.GetProperty("data").GetProperty("items").GetArrayLength(), Is.EqualTo(0),
            "删除后分页不应再返回该任务");
    }

    [Test]
    public async Task Job_Create_InvalidCronAndInvalidJobType_Rejected()
    {
        // 非法 Cron → 400903
        var invalidCron = await SendAsync(HttpMethod.Post, "/api/monitor/job", new
        {
            jobName = NewJobName(),
            jobGroup = "DEFAULT",
            cronExpression = "not-a-cron",
            jobType = NoOpJob.TypeFullName,
            status = 0,
            concurrent = false
        });
        var invalidCronBody = await ReadBodyAsync(invalidCron);

        // 非法任务类型（不存在）→ 400902
        var missingType = await SendAsync(HttpMethod.Post, "/api/monitor/job", new
        {
            jobName = NewJobName(),
            jobGroup = "DEFAULT",
            cronExpression = "0/10 * * * * ?",
            jobType = "Infrastructure.Scheduling.NotExistJob, Infrastructure",
            status = 0,
            concurrent = false
        });
        var missingTypeBody = await ReadBodyAsync(missingType);

        // 非法任务类型（存在但未实现 IJob）→ 400902
        var notIJob = await SendAsync(HttpMethod.Post, "/api/monitor/job", new
        {
            jobName = NewJobName(),
            jobGroup = "DEFAULT",
            cronExpression = "0/10 * * * * ?",
            jobType = "Application.Services.SysJobService, Application",
            status = 0,
            concurrent = false
        });
        var notIJobBody = await ReadBodyAsync(notIJob);

        Assert.Multiple(() =>
        {
            Assert.That(invalidCron.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(invalidCronBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400903));
            Assert.That(missingType.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(missingTypeBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400902));
            Assert.That(notIJob.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(notIJobBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400902));
        });
    }

    [Test]
    public async Task Job_Update_ReschedulesAndDeleteMissingJobReturns404BusinessCode()
    {
        // 更新：修改 Cron 与备注后仍可正常立即执行
        var jobName = NewJobName();
        await SendForJsonAsync(HttpMethod.Post, "/api/monitor/job", new
        {
            jobName,
            jobGroup = "DEFAULT",
            cronExpression = "0/1 * * * * ?",
            jobType = NoOpJob.TypeFullName,
            status = 0,
            concurrent = false
        });
        var jobId = await ApiTestSupport.GetEntityIdAsync<SysJob>(_db, it => it.JobName == jobName);

        try
        {
            var jobVersion = await ApiTestSupport.GetVersionAsync<SysJob>(_db, jobId);
            await SendAsync(HttpMethod.Put, $"/api/monitor/job/{jobId}/{jobVersion}", new
            {
                jobName,
                jobGroup = "B10_GROUP",
                cronExpression = "0/5 * * * * ?",
                jobType = NoOpJob.TypeFullName,
                status = 1,
                concurrent = false,
                description = "更新后"
            });

            var updated = (await GetJsonAsync($"/api/monitor/job/page?keyword={jobName}"))
                .RootElement.GetProperty("data").GetProperty("items")[0];
            Assert.Multiple(() =>
            {
                Assert.That(updated.GetProperty("jobGroup").GetString(), Is.EqualTo("B10_GROUP"));
                Assert.That(updated.GetProperty("cronExpression").GetString(), Is.EqualTo("0/5 * * * * ?"));
                Assert.That(updated.GetProperty("status").GetInt32(), Is.EqualTo(1));
                Assert.That(updated.GetProperty("description").GetString(), Is.EqualTo("更新后"));
            });

            // 不存在的任务删除 → 400901
            var deleteMissing = await SendAsync(HttpMethod.Delete, $"/api/monitor/job/00000000-0000-0000-0000-000000009999/{Guid.NewGuid()}");
            var deleteMissingBody = await ReadBodyAsync(deleteMissing);
            Assert.Multiple(() =>
            {
                Assert.That(deleteMissing.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(deleteMissingBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400901));
            });
        }
        finally
        {
            await SendAsync(HttpMethod.Delete, $"/api/monitor/job/{jobId}/{await ApiTestSupport.GetVersionAsync<SysJob>(_db, jobId)}");
        }
    }

    // —— 测试辅助 ——

    /// <summary>生成唯一任务名称。</summary>
    private static string NewJobName() => "b10job_" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>轮询任务执行日志直至达到期望条数（超时断言失败）。</summary>
    private async Task<List<JsonElement>> WaitForJobLogsAsync(Guid jobId, int minimumCount, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        List<JsonElement> items = [];
        while (DateTime.UtcNow < deadline)
        {
            var body = await GetJsonAsync($"/api/monitor/job-log/page?jobId={jobId}&pageIndex=1&pageSize=10");
            items = [.. body.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()];
            if (items.Count >= minimumCount)
            {
                return items;
            }

            await Task.Delay(300);
        }

        Assert.Fail($"等待执行日志超时（{timeoutSeconds}s）：期望至少 {minimumCount} 条，实际 {items.Count} 条，jobId={jobId}");
        return items;
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

    /// <summary>发送 JSON 请求（POST / PUT / DELETE 等）。</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? payload = null)
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

    /// <summary>GET 并解析响应（断言成功状态）。</summary>
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
