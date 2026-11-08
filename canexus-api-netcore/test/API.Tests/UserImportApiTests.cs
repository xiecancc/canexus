using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using Application.Contracts.Users;
using Domain.Entities;
using Domain.Services.Security;
using Domain.Shared.Results;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniExcelLibs;
using SqlSugar;

namespace API.Tests;
/// <summary>
/// 用户批量导入（Excel）集成测试：
/// 走真实 HTTP 管道 + 真实 MySQL（super_admin_test），
/// 重点验证合法模板全量入库、行级错误整体拒绝（全量成功语义）与非法文件拒绝。
/// </summary>
[TestFixture]
public class UserImportApiTests
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

    /// <summary>测试套件清理：释放宿主与资源，并清理导入产生的测试用户。</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync()
    {
        await _db.Ado.ExecuteCommandAsync("DELETE FROM sys_user_role WHERE UserId IN (SELECT Id FROM sys_user WHERE UserName LIKE 'import_%')");
        await _db.Ado.ExecuteCommandAsync("DELETE FROM sys_user WHERE UserName LIKE 'import_%'");
        _httpClient.Dispose();
        _db.Dispose();
        (_cache as IDisposable)?.Dispose();
        _factory.Dispose();
    }

    // —— 导入场景 ——

    /// <summary>验证合法模板全量入库：回执成功数与行数一致，用户可在库中按用户名检索到。</summary>
    [Test]
    public async Task Import_ValidRows_InsertsAll()
    {
        var rows = new List<UserImportRow>
        {
            new() { UserName = "import_ok_a", NickName = "导入甲", Email = "import-a@test.local" },
            new() { UserName = "import_ok_b", NickName = "导入乙", Password = "Import@2026" }
        };
        var result = await ImportAsync(rows);

        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("code").GetInt32(), Is.EqualTo(Result.SuccessCode));
            Assert.That(result.GetProperty("data").GetProperty("totalCount").GetInt32(), Is.EqualTo(2));
            Assert.That(result.GetProperty("data").GetProperty("successCount").GetInt32(), Is.EqualTo(2));
            Assert.That(result.GetProperty("data").GetProperty("failureCount").GetInt32(), Is.EqualTo(0));
        });

        var inserted = await _db.Queryable<SysUser>().Where(u => u.UserName == "import_ok_a").FirstAsync();
        Assert.Multiple(() =>
        {
            Assert.That(inserted, Is.Not.Null, "合法行应已入库");
            Assert.That(inserted.NickName, Is.EqualTo("导入甲"));
            Assert.That(inserted.Email, Is.EqualTo("import-a@test.local"));
        });
    }

    /// <summary>验证全量成功语义：含非法行（用户名格式错误）时整体不入库，合法行也不写入。</summary>
    [Test]
    public async Task Import_AnyInvalidRow_RejectsAll()
    {
        var rows = new List<UserImportRow>
        {
            new() { UserName = "import_ok_c", NickName = "合法行不应入库" },
            new() { UserName = "bad name!", NickName = "非法行" }
        };
        var result = await ImportAsync(rows);

        var data = result.GetProperty("data");
        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("code").GetInt32(), Is.EqualTo(Result.SuccessCode));
            Assert.That(data.GetProperty("failureCount").GetInt32(), Is.GreaterThanOrEqualTo(1));
            Assert.That(data.GetProperty("successCount").GetInt32(), Is.EqualTo(0), "存在错误行时应整体拒绝");
        });
        var errors = data.GetProperty("errors");
        Assert.That(errors.GetArrayLength(), Is.GreaterThanOrEqualTo(1), "应返回行级错误明细");
        var firstError = errors[0];
        Assert.Multiple(() =>
        {
            Assert.That(firstError.GetProperty("row").GetInt32(), Is.EqualTo(3), "错误行号应为 Excel 第 3 行（1 表头 + 第 2 数据行）");
            Assert.That(firstError.GetProperty("reason").GetString(), Does.Contain("格式非法"));
        });

        var notInserted = await _db.Queryable<SysUser>().Where(u => u.UserName == "import_ok_c").CountAsync();
        Assert.That(notInserted, Is.EqualTo(0), "合法行在整体拒绝时也不应入库");
    }

    /// <summary>验证库内用户名冲突：与已有用户重名的行报行级错误且整体不入库。</summary>
    [Test]
    public async Task Import_DuplicateUserNameInDb_RejectsAll()
    {
        var rows = new List<UserImportRow>
        {
            new() { UserName = "import_ok_d", NickName = "冲突场景合法行" },
            new() { UserName = AdminUserName, NickName = "与超管重名" }
        };
        var result = await ImportAsync(rows);

        var data = result.GetProperty("data");
        Assert.Multiple(() =>
        {
            Assert.That(data.GetProperty("failureCount").GetInt32(), Is.EqualTo(1));
            Assert.That(data.GetProperty("errors")[0].GetProperty("reason").GetString(), Does.Contain("已存在于系统中"));
        });

        var notInserted = await _db.Queryable<SysUser>().Where(u => u.UserName == "import_ok_d").CountAsync();
        Assert.That(notInserted, Is.EqualTo(0));
    }

    /// <summary>验证非法文件（非 xlsx 内容）返回 400207。</summary>
    [Test]
    public async Task Import_InvalidFile_ReturnsFileInvalid()
    {
        var response = await UploadFileAsync("broken.xlsx", "not-a-real-xlsx"u8.ToArray());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body.GetProperty("code").GetInt32(), Is.EqualTo(400207));
        });
    }

    // —— 公共辅助 ——

    /// <summary>构建 xlsx（MiniExcel 写入内存流）并上传导入。</summary>
    /// <param name="rows">导入行。</param>
    /// <returns>导入响应 JSON。</returns>
    private async Task<JsonElement> ImportAsync(List<UserImportRow> rows)
    {
        using var workbook = new MemoryStream();
        await MiniExcel.SaveAsAsync(workbook, rows);
        var response = await UploadFileAsync("import.xlsx", workbook.ToArray());
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"导入请求应成功：{await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>上传文件的原始 HTTP 请求（multipart/form-data → POST /api/system/user/import）。</summary>
    /// <param name="fileName">文件名。</param>
    /// <param name="payload">文件内容。</param>
    /// <returns>HTTP 响应。</returns>
    private async Task<HttpResponseMessage> UploadFileAsync(string fileName, byte[] payload)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(payload);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/system/user/import");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        request.Content = form;
        return await _httpClient.SendAsync(request);
    }

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
