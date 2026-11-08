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
/// 岗位管理（P2）集成测试：走真实 HTTP 管道 + 真实 MySQL，
/// 验证 CRUD 全流程、岗位编码唯一性（401302）、字段校验（401303）、
/// 删除引用守卫（401304）与解除引用后删除，以及下拉仅返回启用岗位。
/// </summary>
[TestFixture]
public class PostApiTests
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
    private static string _adminToken = null!;

    /// <summary>测试套件初始化：启动测试宿主并走真实登录链路获取令牌。</summary>
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

        var captcha = await _httpClient.GetAsync("/api/auth/captcha");
        captcha.EnsureSuccessStatusCode();
        var captchaBody = await ReadBodyAsync(captcha);
        var captchaId = captchaBody.RootElement.GetProperty("data").GetProperty("captchaId").GetString()!;
        var cache = _factory.Services.GetRequiredService<ICache>();
        var code = await cache.GetAsync<string>(ICaptchaService.CacheKeyPrefix + captchaId);
        Assert.That(code, Is.Not.Null, "验证码应已写入缓存");

        var login = await _httpClient.PostAsJsonAsync("/api/auth/login", new
        {
            userName = AdminUserName,
            password = AdminPassword,
            captchaId,
            captchaCode = code
        });
        var loginBody = await ReadBodyAsync(login);
        Assert.That(login.IsSuccessStatusCode, Is.True, $"admin 登录应成功：{loginBody.RootElement.GetRawText()}");
        _adminToken = loginBody.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    /// <summary>测试套件清理：释放宿主与资源。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
        _db.Dispose();
        _factory.Dispose();
    }

    // —— CRUD 全流程 ——

    /// <summary>验证岗位全流程：创建 → 分页命中 → 详情 → 更新 → 删除后分页不可见。</summary>
    [Test]
    public async Task Post_FullFlow_Create_Page_Detail_Update_Delete()
    {
        var postCode = NewPostCode();

        var createBody = await SendForJsonAsync(HttpMethod.Post, "/api/system/post", new
        {
            postName = "集成测试岗位",
            postCode,
            orderNum = 66,
            status = 1,
            description = "P2 集成测试"
        });
        var postId = await ApiTestSupport.GetEntityIdAsync<SysPost>(_db, it => it.PostCode == postCode);

        // 分页命中（编码模糊筛选）
        var page = await GetJsonAsync($"/api/system/post/page?postCode={postCode[..8]}");
        var items = page.RootElement.GetProperty("data").GetProperty("items");
        Assert.That(items.GetArrayLength(), Is.EqualTo(1), "编码前缀应精确命中新建岗位");
        var row = items[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("id").GetGuid(), Is.EqualTo(postId));
            Assert.That(row.GetProperty("postName").GetString(), Is.EqualTo("集成测试岗位"));
            Assert.That(row.GetProperty("postCode").GetString(), Is.EqualTo(postCode));
            Assert.That(row.GetProperty("orderNum").GetInt32(), Is.EqualTo(66));
            Assert.That(row.GetProperty("status").GetInt32(), Is.EqualTo(1));
        });

        // 详情
        var detail = await GetJsonAsync($"/api/system/post/{postId}");
        Assert.That(detail.RootElement.GetProperty("data").GetProperty("postCode").GetString(), Is.EqualTo(postCode));

        // 更新（停用 + 改名，乐观锁回读版本号）
        var version = await ApiTestSupport.GetVersionAsync<SysPost>(_db, postId);
        await SendAsync(HttpMethod.Put, $"/api/system/post/{postId}/{version}", new
        {
            postName = "集成测试岗位V2",
            postCode,
            orderNum = 67,
            status = 0,
            description = (string?)null
        });
        var updated = await GetJsonAsync($"/api/system/post/{postId}");
        var updatedData = updated.RootElement.GetProperty("data");
        Assert.Multiple(() =>
        {
            Assert.That(updatedData.GetProperty("postName").GetString(), Is.EqualTo("集成测试岗位V2"));
            Assert.That(updatedData.GetProperty("status").GetInt32(), Is.EqualTo(0));
            Assert.That(updatedData.GetProperty("orderNum").GetInt32(), Is.EqualTo(67));
        });

        // 删除 → 分页不可见
        await SendAsync(HttpMethod.Delete, $"/api/system/post/{postId}/{await ApiTestSupport.GetVersionAsync<SysPost>(_db, postId)}");
        var afterDelete = await GetJsonAsync($"/api/system/post/page?postCode={postCode[..8]}");
        Assert.That(afterDelete.RootElement.GetProperty("data").GetProperty("total").GetInt64(), Is.EqualTo(0), "删除后分页应不可见");
    }

    // —— 唯一性与字段校验 ——

    /// <summary>验证重复岗位编码被拒绝并返回 401302、非法字段返回 401303。</summary>
    [Test]
    public async Task Post_Create_DuplicateCode_And_InvalidFields_Rejected()
    {
        var postCode = NewPostCode();
        await SendForJsonAsync(HttpMethod.Post, "/api/system/post", new
        {
            postName = "唯一性测试岗位",
            postCode,
            orderNum = 1,
            status = 1
        });

        // 重复编码
        var duplicate = await SendAsync(HttpMethod.Post, "/api/system/post", new
        {
            postName = "另一个岗位",
            postCode,
            orderNum = 2,
            status = 1
        });
        var duplicateBody = await ReadBodyAsync(duplicate);
        Assert.That(duplicateBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(401302), "重复编码应返回 401302");

        // 非法编码字符
        var badCode = await SendAsync(HttpMethod.Post, "/api/system/post", new
        {
            postName = "编码非法岗位",
            postCode = "bad code!@#",
            orderNum = 1,
            status = 1
        });
        Assert.That((await ReadBodyAsync(badCode)).RootElement.GetProperty("code").GetInt32(), Is.EqualTo(401303), "非法编码应返回 401303");

        // 名称空白
        var blankName = await SendAsync(HttpMethod.Post, "/api/system/post", new
        {
            postName = "   ",
            postCode = NewPostCode(),
            orderNum = 1,
            status = 1
        });
        Assert.That((await ReadBodyAsync(blankName)).RootElement.GetProperty("code").GetInt32(), Is.EqualTo(401303), "空白名称应返回 401303");
    }

    // —— 删除引用守卫 ——

    /// <summary>验证岗位被用户引用时删除返回 401304，解除引用后可删除。</summary>
    [Test]
    public async Task Post_Delete_InUse_Guard_401304_Then_Unlink_Delete()
    {
        var postCode = NewPostCode();
        await SendForJsonAsync(HttpMethod.Post, "/api/system/post", new
        {
            postName = "引用守卫岗位",
            postCode,
            orderNum = 1,
            status = 1
        });
        var postId = await ApiTestSupport.GetEntityIdAsync<SysPost>(_db, it => it.PostCode == postCode);

        // 直接插入用户-岗位关联（模拟用户已分配该岗位）
        var userId = (await _db.Queryable<SysUser>().FirstAsync(it => it.UserName == "admin")).Id;
        await _db.Insertable(new SysUserPost { UserId = userId, PostId = postId }).ExecuteCommandAsync();

        // 删除应被拒绝
        var version = await ApiTestSupport.GetVersionAsync<SysPost>(_db, postId);
        var delete = await SendAsync(HttpMethod.Delete, $"/api/system/post/{postId}/{version}");
        var deleteBody = await ReadBodyAsync(delete);
        Assert.That(deleteBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(401304), "被引用岗位删除应返回 401304");

        // 解除引用后可删除
        await _db.Deleteable<SysUserPost>().Where(ur => ur.PostId == postId).ExecuteCommandAsync();
        var versionAfter = await ApiTestSupport.GetVersionAsync<SysPost>(_db, postId);
        await SendAsync(HttpMethod.Delete, $"/api/system/post/{postId}/{versionAfter}");
        var page = await GetJsonAsync($"/api/system/post/page?postCode={postCode[..8]}");
        Assert.That(page.RootElement.GetProperty("data").GetProperty("total").GetInt64(), Is.EqualTo(0), "解除引用后删除应成功");
    }

    // —— 下拉 ——

    /// <summary>验证岗位下拉仅返回启用岗位。</summary>
    [Test]
    public async Task Post_Dropdown_OnlyEnabled()
    {
        var enabledCode = NewPostCode();
        var disabledCode = NewPostCode();
        await SendForJsonAsync(HttpMethod.Post, "/api/system/post", new
        {
            postName = "下拉启用岗位",
            postCode = enabledCode,
            orderNum = 1,
            status = 1
        });
        await SendForJsonAsync(HttpMethod.Post, "/api/system/post", new
        {
            postName = "下拉停用岗位",
            postCode = disabledCode,
            orderNum = 2,
            status = 0
        });

        var dropdown = await GetJsonAsync("/api/system/post/dropdown");
        var items = dropdown.RootElement.GetProperty("data");
        var codes = items.EnumerateArray()
            .Select(item => item.GetProperty("postCode").GetString())
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(codes, Does.Contain(enabledCode), "启用岗位应出现在下拉中");
            Assert.That(codes, Does.Not.Contain(disabledCode), "停用岗位不应出现在下拉中");
        });
    }

    // —— 测试辅助 ——

    /// <summary>生成唯一岗位编码（字母开头 + GUID 片段，满足编码正则）。</summary>
    private static string NewPostCode() => "P" + Guid.NewGuid().ToString("N")[..10];

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
