namespace API.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using Domain.Services.Security;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

/// <summary>
/// 通知公告（B12）集成测试：
/// 走真实 HTTP 管道 + 真实 MySQL（默认库 super_admin_test，SUPER_ADMIN_TEST_CONN 可覆盖），
/// 重点验证 CRUD 全流程（列表不含正文 / 详情含正文）、字段校验业务码、
/// 最新公告仅含正常状态且按时间倒序收敛数量，以及 latest 仅要求登录不挂权限。
/// </summary>
[TestFixture]
public class NoticeApiTests
{
    /// <summary>默认测试连接串（root 密码为本机约定值）。</summary>
    private const string DefaultConnectionString =
        "server=localhost;port=3306;database=super_admin_test;user id=root;password=***REMOVED***;CharSet=utf8mb4;AllowPublicKeyRetrieval=true;SslMode=none";

    private const string AdminUserName = "admin";
    private const string AdminPassword = "admin123";

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _httpClient = null!;
    private static ISqlSugarClient _db = null!;
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

        // 走真实登录链路：验证码从 IMemoryCache 读取明文
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

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
        _db.Dispose();
        _factory.Dispose();
    }

    // —— CRUD 全流程 ——

    [Test]
    public async Task Notice_FullFlow_ListWithoutContent_DetailWithContent()
    {
        var title = NewNoticeTitle();

        // 创建 + 返回新 Id
        var createBody = await SendForJsonAsync(HttpMethod.Post, "/api/system/notice", new
        {
            title,
            noticeType = 1,
            content = "<p>B12 集成测试正文</p>",
            status = 1,
            description = "B12 集成测试"
        });
        var noticeId = createBody.RootElement.GetProperty("data").GetGuid();
        Assert.That(noticeId, Is.Not.EqualTo(Guid.Empty), "创建应返回公告 Id");

        // 分页可见：列表行不含正文（content 为 null 以减小载荷）
        var page = await GetJsonAsync($"/api/system/notice/page?title={Uri.EscapeDataString(title)}");
        var items = page.RootElement.GetProperty("data").GetProperty("items");
        Assert.That(items.GetArrayLength(), Is.EqualTo(1), "标题关键字应精确命中新建公告");
        var row = items[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("id").GetGuid(), Is.EqualTo(noticeId));
            Assert.That(row.GetProperty("title").GetString(), Is.EqualTo(title));
            Assert.That(row.GetProperty("noticeType").GetInt32(), Is.EqualTo(1));
            Assert.That(row.GetProperty("status").GetInt32(), Is.EqualTo(1));
            Assert.That(row.GetProperty("description").GetString(), Is.EqualTo("B12 集成测试"));
            Assert.That(row.TryGetProperty("createdAt", out _), Is.True, "列表应包含 createdAt");
            Assert.That(row.GetProperty("content").ValueKind, Is.EqualTo(JsonValueKind.Null), "列表不应返回正文");
        });

        // 详情含正文
        var detail = await GetJsonAsync($"/api/system/notice/{noticeId}");
        Assert.Multiple(() =>
        {
            Assert.That(detail.RootElement.GetProperty("data").GetProperty("content").GetString(),
                Is.EqualTo("<p>B12 集成测试正文</p>"), "详情应返回正文");
        });

        // 更新（标题 / 类型 / 内容 / 状态 / 备注）→ 详情同步变化
        await SendForJsonAsync(HttpMethod.Put, $"/api/system/notice/{noticeId}", new
        {
            title = title + "V2",
            noticeType = 2,
            content = "<p>更新后的正文</p>",
            status = 0,
            description = (string?)null
        });
        var updated = await GetJsonAsync($"/api/system/notice/{noticeId}");
        var updatedData = updated.RootElement.GetProperty("data");
        Assert.Multiple(() =>
        {
            Assert.That(updatedData.GetProperty("title").GetString(), Is.EqualTo(title + "V2"));
            Assert.That(updatedData.GetProperty("noticeType").GetInt32(), Is.EqualTo(2));
            Assert.That(updatedData.GetProperty("content").GetString(), Is.EqualTo("<p>更新后的正文</p>"));
            Assert.That(updatedData.GetProperty("status").GetInt32(), Is.EqualTo(0));
        });

        // 删除 → 分页不可见
        await SendForJsonAsync(HttpMethod.Delete, $"/api/system/notice/{noticeId}", null!);
        var afterDelete = await GetJsonAsync($"/api/system/notice/page?title={Uri.EscapeDataString(title)}");
        Assert.That(afterDelete.RootElement.GetProperty("data").GetProperty("total").GetInt64(), Is.EqualTo(0), "删除后分页应不可见");
    }

    // —— 字段校验与不存在 ——

    [Test]
    public async Task Notice_CreateValidation_InvalidFieldsRejectedWith401002()
    {
        var title = NewNoticeTitle();

        // 类型非法（非 1/2）
        var badType = await SendAsync(HttpMethod.Post, "/api/system/notice", new
        {
            title,
            noticeType = 3,
            content = "x",
            status = 1
        });
        var badTypeBody = await ReadBodyAsync(badType);

        // 状态非法（非 0/1）
        var badStatus = await SendAsync(HttpMethod.Post, "/api/system/notice", new
        {
            title,
            noticeType = 1,
            content = "x",
            status = 2
        });
        var badStatusBody = await ReadBodyAsync(badStatus);

        // 标题空白
        var blankTitle = await SendAsync(HttpMethod.Post, "/api/system/notice", new
        {
            title = "   ",
            noticeType = 1,
            content = "x",
            status = 1
        });
        var blankTitleBody = await ReadBodyAsync(blankTitle);

        // 内容空白
        var blankContent = await SendAsync(HttpMethod.Post, "/api/system/notice", new
        {
            title,
            noticeType = 1,
            content = "  ",
            status = 1
        });
        var blankContentBody = await ReadBodyAsync(blankContent);

        // 不存在的详情 / 更新 / 删除 → 401001
        var missingDetail = await SendAsync(HttpMethod.Get, "/api/system/notice/00000000-0000-0000-0000-000000009999", null);
        var missingDetailBody = await ReadBodyAsync(missingDetail);
        var missingUpdate = await SendAsync(HttpMethod.Put, "/api/system/notice/00000000-0000-0000-0000-000000009999", new
        {
            title,
            noticeType = 1,
            content = "x",
            status = 1
        });
        var missingUpdateBody = await ReadBodyAsync(missingUpdate);
        var missingDelete = await SendAsync(HttpMethod.Delete, "/api/system/notice/00000000-0000-0000-0000-000000009999", null);
        var missingDeleteBody = await ReadBodyAsync(missingDelete);

        Assert.Multiple(() =>
        {
            Assert.That(badType.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(badTypeBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(401002));
            Assert.That(badStatus.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(badStatusBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(401002));
            Assert.That(blankTitle.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(blankTitleBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(401002));
            Assert.That(blankContent.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(blankContentBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(401002));
            Assert.That(missingDetail.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(missingDetailBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(401001));
            Assert.That(missingUpdate.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(missingUpdateBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(401001));
            Assert.That(missingDelete.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(missingDeleteBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(401001));
        });
    }

    // —— 最新公告（首页卡片）——

    [Test]
    public async Task Notice_Latest_OnlyEnabledOrderedDescAndCappedAt20()
    {
        // 种子公告：正常状态的「系统上线通知」应可见，关闭状态的「系统维护公告」应被排除
        var seeded = await GetJsonAsync("/api/system/notice/latest?count=20");
        var seededItems = seeded.RootElement.GetProperty("data");
        var seededTitles = seededItems.EnumerateArray()
            .Select(i => i.GetProperty("title").GetString())
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(seededTitles, Does.Contain("系统上线通知"), "正常状态的种子公告应出现在最新列表");
            Assert.That(seededTitles, Does.Not.Contain("系统维护公告"), "关闭状态的种子公告不应出现在最新列表");
        });

        // 连续创建 21 条正常公告（创建时间单调不减、Id 单调递增）
        var createdIds = new List<Guid>();
        for (var i = 1; i <= 21; i++)
        {
            var id = (await SendForJsonAsync(HttpMethod.Post, "/api/system/notice", new
            {
                title = NewNoticeTitle(),
                noticeType = 1,
                content = $"B12 数量收敛测试 {i}",
                status = 1
            })).RootElement.GetProperty("data").GetGuid();
            createdIds.Add(id);
        }

        try
        {
            // 上限收敛：count=999 → 仅返回 20 条
            var capped = await GetJsonAsync("/api/system/notice/latest?count=999");
            var cappedItems = capped.RootElement.GetProperty("data");
            Assert.That(cappedItems.GetArrayLength(), Is.EqualTo(20), "最新公告数量应收敛为上限 20");

            // 倒序：按 CreatedAt 倒序；同秒并列时 Guid 主键排序随机，故仅校验返回项均为本次新建公告（均晚于种子公告）
            var cappedIds = cappedItems.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
            Assert.That(cappedIds, Is.SubsetOf(createdIds), "最新列表应只包含本次新建的公告（均晚于种子公告）");

            // 指定条数：count=2 → 2 条，简项不含正文
            var limited = await GetJsonAsync("/api/system/notice/latest?count=2");
            var limitedItems = limited.RootElement.GetProperty("data");
            Assert.Multiple(() =>
            {
                Assert.That(limitedItems.GetArrayLength(), Is.EqualTo(2));
                Assert.That(limitedItems[0].GetProperty("title").GetString(), Is.Not.Null);
                Assert.That(limitedItems[0].TryGetProperty("content", out _), Is.False, "最新公告简项不应包含正文");
            });
        }
        finally
        {
            // 清理测试公告，避免污染其他用例与重复运行
            foreach (var id in createdIds)
            {
                await SendAsync(HttpMethod.Delete, $"/api/system/notice/{id}", null);
            }
        }
    }

    // —— 认证要求（latest 不挂权限但仍需登录）——

    [Test]
    public async Task Notice_Latest_WithoutToken_ReturnsUnified401()
    {
        var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync("/api/system/notice/latest");
        var body = await ReadBodyAsync(response);
        anonymous.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "未登录访问 latest 应返回 401");
            Assert.That(body.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(40100), "401 响应应为统一业务码 AuthUnauthorized");
        });
    }

    // —— 测试辅助 ——

    /// <summary>生成唯一公告标题（GUID 前缀避免历史数据干扰）。</summary>
    private static string NewNoticeTitle() => "B12_" + Guid.NewGuid().ToString("N")[..10];

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
