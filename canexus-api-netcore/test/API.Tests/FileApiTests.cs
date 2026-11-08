using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using Domain.Services.Security;
using Domain.Shared.Results;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace API.Tests;
/// <summary>
/// 文件存储（系统模块）集成测试：
/// 走真实 HTTP 管道 + 真实 MySQL（super_admin_test）+ 本地磁盘存储（临时根目录，套件结束清理），
/// 重点验证上传白名单与大小上限校验、元数据分页、下载往返一致性与软删除。
/// </summary>
[TestFixture]
public class FileApiTests
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static ICache _cache = null!;
    private static string _adminToken = null!;
    private static string _uploadRoot = null!;

    /// <summary>测试套件初始化：启动测试宿主（存储根目录指向临时目录、上限压到 1MB 便于超限测试）并以 admin 登录。</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = ApiTestSupport.TestConnectionString;
        var apiContentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "API"));
        _uploadRoot = Path.Combine(Path.GetTempPath(), "super-admin-test-uploads", Guid.NewGuid().ToString("N"));
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
                        ["FileStorage:RootPath"] = _uploadRoot,
                        ["FileStorage:MaxSizeMB"] = "1" // 压低上限便于超限用例（默认 10MB 构造大载荷过慢）
                    });
                });
            });

        _httpClient = _factory.CreateClient();
        _db = _factory.Services.GetRequiredService<ISqlSugarClient>();
        _cache = _factory.Services.GetRequiredService<ICache>();
        _adminToken = await LoginAsync(AdminUserName, AdminPassword);
    }

    /// <summary>测试套件清理：释放宿主与资源，并清理临时上传目录。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
        _db.Dispose();
        (_cache as IDisposable)?.Dispose();
        _factory.Dispose();
        if (Directory.Exists(_uploadRoot))
        {
            Directory.Delete(_uploadRoot, recursive: true);
        }
    }

    // —— 上传与下载 ——

    /// <summary>验证合法文件上传成功：返回 Id，元数据可分页检索，下载内容与上传字节一致。</summary>
    [Test]
    public async Task Upload_Then_Page_And_Download_RoundTrip()
    {
        var payload = "canexus file storage roundtrip 测试内容 2026"u8.ToArray();
        var uploaded = await UploadAsync("roundtrip-test.txt", "text/plain", payload);

        Assert.Multiple(() =>
        {
            Assert.That(uploaded.GetProperty("code").GetInt32(), Is.EqualTo(Result.SuccessCode));
            Assert.That(uploaded.GetProperty("data").GetProperty("fileName").GetString(), Is.EqualTo("roundtrip-test.txt"));
            Assert.That(uploaded.GetProperty("data").GetProperty("sizeBytes").GetInt64(), Is.EqualTo(payload.Length));
        });
        var fileId = uploaded.GetProperty("data").GetProperty("id").GetGuid();

        // 分页可检索
        var page = await GetJsonAsync("/api/system/file/page?pageSize=50");
        var items = page.RootElement.GetProperty("data").GetProperty("items");
        var match = items.EnumerateArray().FirstOrDefault(i => i.GetProperty("id").GetGuid() == fileId);
        Assert.Multiple(() =>
        {
            Assert.That(match.ValueKind, Is.EqualTo(JsonValueKind.Object), "上传后应出现在分页列表");
            Assert.That(match.GetProperty("fileName").GetString(), Is.EqualTo("roundtrip-test.txt"));
            Assert.That(match.GetProperty("storageProvider").GetString(), Is.EqualTo("local"));
        });

        // 下载内容与上传一致
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/system/file/{fileId}/download");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        using var response = await _httpClient.SendAsync(request);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/octet-stream"),
                "下载响应固定为二进制类型，不回显库中 MIME");
            Assert.That(response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff), Is.True);
            Assert.That(nosniff, Does.Contain("nosniff"));
            Assert.That(response.Content.Headers.ContentDisposition?.FileName, Is.Not.Null.And.Not.Empty);
        });
        var downloaded = await response.Content.ReadAsByteArrayAsync();
        Assert.That(downloaded, Is.EqualTo(payload), "下载字节应与上传内容一致");
    }

    /// <summary>验证扩展名白名单：.exe 上传被拒绝（401202），且不产生元数据。</summary>
    [Test]
    public async Task Upload_Rejects_Disallowed_Extension()
    {
        var response = await UploadRawAsync("evil.exe", "application/octet-stream", [1, 2, 3, 4]);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body.GetProperty("code").GetInt32(), Is.EqualTo(401202));
            Assert.That(body.GetProperty("message").GetString(), Does.Contain("不支持的文件类型"));
        });
    }

    /// <summary>伪装后缀被内容签名校验拒绝（声明 .png 但内容是文本）。</summary>
    [Test]
    public async Task Upload_Rejects_ContentSignatureMismatch()
    {
        var response = await UploadRawAsync("disguised.png", "image/png", "this is not a png"u8.ToArray());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body.GetProperty("code").GetInt32(), Is.EqualTo(401202));
            Assert.That(body.GetProperty("message").GetString(), Does.Contain("与文件内容不符"));
        });
    }

    /// <summary>客户端声明的 MIME 不参与判定：落库 ContentType 由内容签名推导。</summary>
    [Test]
    public async Task Upload_Ignores_ClientDeclaredMime()
    {
        var uploaded = await UploadAsync("mime-probe.txt", "text/html", "plain text payload"u8.ToArray());
        Assert.That(uploaded.GetProperty("code").GetInt32(), Is.EqualTo(Result.SuccessCode));
        var fileId = uploaded.GetProperty("data").GetProperty("id").GetGuid();

        var detail = await GetJsonAsync($"/api/system/file/{fileId}");
        Assert.That(detail.RootElement.GetProperty("data").GetProperty("contentType").GetString(),
            Is.EqualTo("text/plain"), "客户端声明 text/html 不得污染落库 MIME");
    }

    /// <summary>验证大小上限：超过 1MB 的合法扩展名文件被拒绝（401203）。</summary>
    [Test]
    public async Task Upload_Rejects_Oversize_File()
    {
        var big = new byte[1024 * 1024 + 1]; // 1MB + 1 字节
        var response = await UploadRawAsync("big.txt", "text/plain", big);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body.GetProperty("code").GetInt32(), Is.EqualTo(401203));
            Assert.That(body.GetProperty("message").GetString(), Does.Contain("超出上限"));
        });
    }

    /// <summary>边界守卫：超过业务上限但未超过 Kestrel 线级上限（MaxSizeMB+1MB）时，由业务校验以 401203 拒绝。</summary>
    [Test]
    public async Task Upload_BetweenBusinessAndKestrelLimit_RejectedByBusinessRule()
    {
        var payload = new byte[1024 * 1024 + 512 * 1024]; // 1.5MB：超 1MB 业务上限，低于 2MB Kestrel 上限
        var response = await UploadRawAsync("mid-size.txt", "text/plain", payload);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body.GetProperty("code").GetInt32(), Is.EqualTo(401203));
        });
    }

    /// <summary>
    /// 核心回归（P0-S3）：请求体超过 Kestrel 线级上限（按 FileStorage:MaxSizeMB+1MB 配置）时，
    /// 必须在进入业务校验前以 413 拒绝并返回统一业务码 401203（修复前端点上限为 1GB，大载荷被完整缓冲后才被业务拒绝）。
    /// </summary>
    [Test]
    public async Task Upload_BodyOverKestrelLimit_Returns413WithBusinessCode()
    {
        var payload = new byte[2 * 1024 * 1024 + 64 * 1024]; // 2MB+64KB：超过 MaxSizeMB=1 时的 Kestrel 上限 2MB
        var response = await UploadRawAsync("wire-limit.txt", "text/plain", payload);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge),
                "超过 Kestrel 请求体上限应返回 413");
            Assert.That(body.GetProperty("code").GetInt32(), Is.EqualTo(401203),
                "413 响应应携带统一业务错误码 401203");
        });
    }

    /// <summary>验证软删除：删除后分页不可见，详情返回 401201。</summary>
    [Test]
    public async Task Delete_SoftHides_Metadata()
    {
        var uploaded = await UploadAsync("to-be-deleted.txt", "text/plain", "delete me"u8.ToArray());
        var fileId = uploaded.GetProperty("data").GetProperty("id").GetGuid();
        var detail = await GetJsonAsync($"/api/system/file/{fileId}");
        var version = detail.RootElement.GetProperty("data").GetProperty("version").GetGuid();

        // 软删除（乐观锁版本进路由）
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/system/file/{fileId}/{version}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        using var deleteResponse = await _httpClient.SendAsync(deleteRequest);
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var after = await GetJsonAsync($"/api/system/file/{fileId}");
        Assert.Multiple(() =>
        {
            Assert.That(after.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(401201), "软删除后详情应返回 401201");
            Assert.That(after.RootElement.GetProperty("message").GetString(), Does.Contain("文件"));
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

    /// <summary>上传文件并返回响应（multipart/form-data）。</summary>
    /// <param name="fileName">文件名。</param>
    /// <param name="contentType">MIME 类型。</param>
    /// <param name="payload">文件内容。</param>
    /// <returns>HTTP 响应（JSON 正文）。</returns>
    private async Task<JsonElement> UploadAsync(string fileName, string contentType, byte[] payload)
    {
        var response = await UploadRawAsync(fileName, contentType, payload);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>上传文件的原始 HTTP 请求（multipart/form-data）。</summary>
    /// <param name="fileName">文件名。</param>
    /// <param name="contentType">MIME 类型。</param>
    /// <param name="payload">文件内容。</param>
    /// <returns>HTTP 响应。</returns>
    private async Task<HttpResponseMessage> UploadRawAsync(string fileName, string contentType, byte[] payload)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(payload);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/system/file/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        request.Content = form;
        return await _httpClient.SendAsync(request);
    }

    /// <summary>GET JSON 请求（携带 admin 令牌）。</summary>
    /// <param name="url">请求地址。</param>
    /// <returns>JSON 正文。</returns>
    private async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        using var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
