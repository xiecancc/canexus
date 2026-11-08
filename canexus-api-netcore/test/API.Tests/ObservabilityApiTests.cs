using System.Net.Http.Json;
using API;
using Domain.Entities;
using Domain.Services.Auth;
using Domain.Shared.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace API.Tests;

/// <summary>
/// 可观测性（P2-B3）集成测试：请求级 TraceId 生成并贯穿 SQL 日志链路。
/// <para>验收口径：慢接口可经响应头 X-Trace-Id 一键定位到同 TraceId 的 SQL 执行日志（事件链路同理）。
/// 日志经 Serilog 真实管道捕获（测试宿主追加文件 sink，不替换宿主日志配置）。</para>
/// </summary>
[TestFixture]
public class ObservabilityApiTests
{
    private const string UserName = "observability_user";
    private const string Password = "Test123456";

    private static readonly string SerilogCapturePath =
        Path.Combine(Path.GetTempPath(), $"serilog-capture-{Guid.NewGuid():N}.log");

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static IPasswordHasher _passwordHasher = null!;

    /// <summary>套件初始化：启动测试宿主（打开 SQL 日志；Serilog 追加独立文件 sink 捕获输出）。</summary>
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
                        ["Database:SqlLogEnabled"] = "true", // 打开 SQL 执行日志，验证 TraceId 贯穿
                        // Serilog 追加独立文件 sink（保留原 Console/File 不动），测试读取该文件断言
                        ["Serilog:WriteTo:2:Name"] = "File",
                        ["Serilog:WriteTo:2:Args:path"] = SerilogCapturePath,
                        ["Serilog:WriteTo:2:Args:rollingInterval"] = "Infinite",
                        ["Serilog:WriteTo:2:Args:shared"] = "true",
                        ["Serilog:WriteTo:2:Args:outputTemplate"] = "{Message:lj}{NewLine}{Exception}"
                    });
                });
            });

        _httpClient = _factory.CreateClient();
        _db = _factory.Services.GetRequiredService<ISqlSugarClient>();
        _passwordHasher = _factory.Services.GetRequiredService<IPasswordHasher>();
    }

    /// <summary>套件清理。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _db.Deleteable<SysUser>().Where(user => user.UserName == UserName).ExecuteCommand();
        _httpClient.Dispose();
        _db.Dispose();
        _factory.Dispose();
        if (File.Exists(SerilogCapturePath))
        {
            try
            {
                File.Delete(SerilogCapturePath);
            }
            catch (IOException)
            {
                // 文件可能仍被 Serilog 文件 sink 短暂持有，忽略（临时捕获文件，残留无害）
            }
        }
    }

    /// <summary>核心断言：每个请求的响应携带 X-Trace-Id 头（32 位十六进制，请求链路的唯一标识）。</summary>
    [Test]
    public async Task Request_ResponseCarriesTraceIdHeader()
    {
        var response = await _httpClient.GetAsync("/api/auth/captcha");
        response.EnsureSuccessStatusCode();

        var traceId = response.Headers.TryGetValues("X-Trace-Id", out var values) ? values.FirstOrDefault() : null;
        Assert.That(traceId, Is.Not.Null, "响应应携带 X-Trace-Id 头");
        Assert.That(traceId!, Does.Match("^[0-9a-f]{32}$"), "TraceId 应为 32 位十六进制");
    }

    /// <summary>核心断言：同一请求的 SQL 执行日志携带同一 TraceId——慢接口可经响应头 TraceId 一键定位到 SQL 链路。</summary>
    [Test]
    public async Task Request_TraceIdAppearsInSqlLog()
    {
        // 走一次真实登录（用户查询 + 登录日志写入等真实 SQL），验证 SQL 日志与请求 TraceId 关联
        var token = await CreateUserAndLoginAsync();
        try
        {
            var traceId = token.TraceId;
            Assert.That(await WaitForSqlLogWithTraceIdAsync(traceId, TimeSpan.FromSeconds(10)),
                Is.True,
                "SqlSugar SQL 日志应携带同一 TraceId（经响应头可一键定位 SQL 链路）");
        }
        finally
        {
            await _db.Deleteable<SysLoginLog>().Where(log => log.UserName == UserName).ExecuteCommandAsync();
            await _db.Deleteable<SysUser>().Where(user => user.UserName == UserName).ExecuteCommandAsync();
        }
    }

    // —— 测试辅助 ——

    /// <summary>幂等创建测试用户并登录，返回 accessToken 与响应 TraceId。</summary>
    private async Task<(string AccessToken, string TraceId)> CreateUserAndLoginAsync()
    {
        var existing = await _db.Queryable<SysUser>().Where(user => user.UserName == UserName).FirstAsync();
        if (existing is null)
        {
            await _db.Insertable(new SysUser
            {
                UserName = UserName,
                NickName = "可观测性测试用户",
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

        var (captchaId, captchaCode) = await GetCaptchaAsync();
        var loginResponse = await _httpClient.PostAsJsonAsync("/api/auth/login",
            new { userName = UserName, password = Password, captchaId, captchaCode });
        if (!loginResponse.IsSuccessStatusCode)
        {
            Assert.Fail($"登录应成功：HTTP {(int)loginResponse.StatusCode}");
        }

        var traceId = loginResponse.Headers.TryGetValues("X-Trace-Id", out var values) ? values.First() : null!;
        var body = await loginResponse.Content.ReadAsStringAsync();
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var accessToken = document.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
        return (accessToken, traceId);
    }

    /// <summary>调用验证码接口并从缓存读取明文验证码。</summary>
    private async Task<(string CaptchaId, string CaptchaCode)> GetCaptchaAsync()
    {
        var response = await _httpClient.GetAsync("/api/auth/captcha");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var captchaId = document.RootElement.GetProperty("data").GetProperty("captchaId").GetString()!;
        var code = await _factory.Services.GetRequiredService<Infrastructure.Shared.Caching.ICache>()
            .GetAsync<string>(Domain.Services.Security.ICaptchaService.CacheKeyPrefix + captchaId);
        Assert.That(code, Is.Not.Null, "验证码应已写入缓存");
        return (captchaId, code!);
    }

    /// <summary>以读写共享方式读取捕获文件（Serilog 文件 sink 可能持有句柄，须 FileShare.ReadWrite）。</summary>
    private static string ReadCaptureFile()
    {
        using var stream = new FileStream(SerilogCapturePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>轮询 Serilog 捕获文件，等待包含指定 TraceId 的 SQL 执行日志出现。</summary>
    private static async Task<bool> WaitForSqlLogWithTraceIdAsync(string traceId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(SerilogCapturePath))
            {
                var content = ReadCaptureFile();
                if (content.Contains("[SqlSugar]", StringComparison.Ordinal)
                    && content.Contains(traceId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            await Task.Delay(200);
        }

        var sample = File.Exists(SerilogCapturePath) ? ReadCaptureFile() : "（捕获文件不存在）";
        var sqlLines = sample.Split('\n')
            .Where(line => line.Contains("[SqlSugar]", StringComparison.Ordinal))
            .Take(3)
            .ToArray();
        Assert.Fail($"未捕获到携带 TraceId={traceId} 的 SQL 日志；SQL 日志 {sqlLines.Length} 条，样例：" +
                    (sqlLines.Length == 0 ? "（无）" : Environment.NewLine + string.Join(Environment.NewLine, sqlLines)));
        return false;
    }
}
