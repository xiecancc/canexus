
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using API;
using Domain.Entities;
using Domain.Services.Security;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace API.Tests;
/// <summary>
/// 字典与参数配置（B7）集成测试：
/// 走真实 HTTP 管道 + 真实 MySQL（默认库 super_admin_test，SUPER_ADMIN_TEST_CONN 可覆盖），
/// 重点验证 CRUD 流程、唯一性 / 不可变性 / 非空删除拒绝、内置参数保护与缓存写后失效。
/// </summary>
[TestFixture]
public class DictConfigApiTests
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

    /// <summary>测试套件初始化：启动测试宿主并走真实登录链路获取令牌。</summary>
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
                        ["Database:ConnectionString"] = connectionString,
                        ["RateLimit:Enabled"] = "false" // 集成测试共享 127.0.0.1 分区且高频请求，关闭限流避免不稳定
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

    /// <summary>测试套件清理：释放宿主与资源。</summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
        _db.Dispose();
        _factory.Dispose();
    }

    // —— 字典类型 ——

    /// <summary>验证字典类型全流程：创建、重复拒绝、内置类型不可修改、非空类型禁止删除。</summary>
    [Test]
    public async Task DictType_FullFlow_DuplicateAndImmutableAndNonEmptyDelete()
    {
        var dictType = NewDictType();

        // 创建 + 分页字段校验
        var createBody = await SendForJsonAsync(HttpMethod.Post, "/api/system/dict-type", new
        {
            dictName = "B7 流程字典",
            dictType,
            description = "B7 集成测试"
        });
        var typeId = await ApiTestSupport.GetEntityIdAsync<SysDictType>(_db, it => it.DictType == dictType);

        var page = await GetJsonAsync($"/api/system/dict-type/page?keyword={dictType}");
        var items = page.RootElement.GetProperty("data").GetProperty("items");
        Assert.That(items.GetArrayLength(), Is.EqualTo(1), "关键字应精确命中新建类型");
        var row = items[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("id").GetGuid(), Is.EqualTo(typeId));
            Assert.That(row.GetProperty("dictName").GetString(), Is.EqualTo("B7 流程字典"));
            Assert.That(row.GetProperty("dictType").GetString(), Is.EqualTo(dictType));
            Assert.That(row.GetProperty("description").GetString(), Is.EqualTo("B7 集成测试"));
            Assert.That(row.TryGetProperty("createdAt", out _), Is.True, "列表应包含 createdAt");
        });

        // 重复 dictType → 400602
        var duplicate = await SendAsync(HttpMethod.Post, "/api/system/dict-type", new
        {
            dictName = "重复类型",
            dictType
        });
        var duplicateBody = await ReadBodyAsync(duplicate);
        Assert.Multiple(() =>
        {
            Assert.That(duplicate.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(duplicateBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400602));
        });

        // 更新（dictType 不变）→ 成功（乐观锁：先回读版本号）
        var typeVersion = await ApiTestSupport.GetVersionAsync<SysDictType>(_db, typeId);
        await SendForJsonAsync(HttpMethod.Put, $"/api/system/dict-type/{typeId}", new
        {
            version = typeVersion,
            dictName = "B7 流程字典V2",
            dictType,
            description = "更新后"
        });
        var updated = (await GetJsonAsync($"/api/system/dict-type/page?keyword={dictType}"))
            .RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.That(updated.GetProperty("dictName").GetString(), Is.EqualTo("B7 流程字典V2"));

        // 更新变更 dictType → 400603（不可变更）
        var immutable = await SendAsync(HttpMethod.Put, $"/api/system/dict-type/{typeId}", new
        {
            version = typeVersion,
            dictName = "B7 流程字典V2",
            dictType = dictType + "x",
            description = (string?)null
        });
        var immutableBody = await ReadBodyAsync(immutable);
        Assert.Multiple(() =>
        {
            Assert.That(immutable.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(immutableBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400603));
        });

        // 插入一条字典数据后删除类型 → 400604（非空拒绝）
        var dataId = await CreateDictDataAsync(dictType, label: "选项A", value: "A");
        var nonEmpty = await SendAsync(HttpMethod.Delete, $"/api/system/dict-type/{typeId}/{await ApiTestSupport.GetVersionAsync<SysDictType>(_db, typeId)}");
        var nonEmptyBody = await ReadBodyAsync(nonEmpty);
        Assert.Multiple(() =>
        {
            Assert.That(nonEmpty.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(nonEmptyBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400604));
        });

        // 清空数据后类型可删除
        await SendAsync(HttpMethod.Delete, $"/api/system/dict-data/{dataId}/{await ApiTestSupport.GetVersionAsync<SysDictData>(_db, dataId)}");
        var deleted = await SendAsync(HttpMethod.Delete, $"/api/system/dict-type/{typeId}/{await ApiTestSupport.GetVersionAsync<SysDictType>(_db, typeId)}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.OK), "无数据的字典类型应可删除");
    }

    // —— 字典数据与缓存刷新 ——

    /// <summary>验证字典数据写操作后按类型查询的缓存立即刷新。</summary>
    [Test]
    public async Task DictData_TypeList_CacheRefreshedImmediatelyAfterWrite()
    {
        var dictType = NewDictType();
        await SendForJsonAsync(HttpMethod.Post, "/api/system/dict-type", new
        {
            dictName = "B7 缓存字典",
            dictType
        });
        var typeId = await ApiTestSupport.GetEntityIdAsync<SysDictType>(_db, it => it.DictType == dictType);

        var dataId = await CreateDictDataAsync(dictType, label: "选项B", value: "B");

        // 首次读取写入缓存
        var first = await GetJsonAsync($"/api/system/dict-data/type/{dictType}");
        var firstItems = first.RootElement.GetProperty("data");
        Assert.That(firstItems.GetArrayLength(), Is.EqualTo(1), "启用数据应返回一条");
        Assert.Multiple(() =>
        {
            Assert.That(firstItems[0].GetProperty("dictLabel").GetString(), Is.EqualTo("选项B"));
            Assert.That(firstItems[0].GetProperty("dictValue").GetString(), Is.EqualTo("B"));
            Assert.That(firstItems[0].GetProperty("tagType").GetInt32(), Is.EqualTo(1));
        });

        // 更新后立即可见（缓存已失效；乐观锁：先回读版本号）
        var dataVersion = await ApiTestSupport.GetVersionAsync<SysDictData>(_db, dataId);
        await SendForJsonAsync(HttpMethod.Put, $"/api/system/dict-data/{dataId}", new
        {
            version = dataVersion,
            dictType,
            dictLabel = "选项B2",
            dictValue = "B",
            tagType = 2,
            orderNum = 1,
            status = 1
        });
        var afterUpdate = await GetJsonAsync($"/api/system/dict-data/type/{dictType}");
        Assert.That(afterUpdate.RootElement.GetProperty("data")[0].GetProperty("dictLabel").GetString(),
            Is.EqualTo("选项B2"), "更新后按类型查询应立即返回新标签");

        // 删除后立即为空
        await SendAsync(HttpMethod.Delete, $"/api/system/dict-data/{dataId}/{await ApiTestSupport.GetVersionAsync<SysDictData>(_db, dataId)}");
        var afterDelete = await GetJsonAsync($"/api/system/dict-data/type/{dictType}");
        Assert.That(afterDelete.RootElement.GetProperty("data").GetArrayLength(), Is.EqualTo(0), "删除后按类型查询应为空");

        await SendAsync(HttpMethod.Delete, $"/api/system/dict-type/{typeId}/{await ApiTestSupport.GetVersionAsync<SysDictType>(_db, typeId)}");
    }

    /// <summary>验证同类型下重复字典标签或键值被拒绝并返回 400606。</summary>
    [Test]
    public async Task DictData_DuplicateLabelOrValue_Rejected400606()
    {
        var dictType = NewDictType();
        await SendForJsonAsync(HttpMethod.Post, "/api/system/dict-type", new
        {
            dictName = "B7 唯一字典",
            dictType
        });
        var typeId = await ApiTestSupport.GetEntityIdAsync<SysDictType>(_db, it => it.DictType == dictType);
        await CreateDictDataAsync(dictType, label: "选项C", value: "C");

        // 同类型下重复标签
        var dupLabel = await SendAsync(HttpMethod.Post, "/api/system/dict-data", new
        {
            dictType,
            dictLabel = "选项C",
            dictValue = "C2",
            tagType = 1,
            orderNum = 1,
            status = 1
        });
        var dupLabelBody = await ReadBodyAsync(dupLabel);

        // 同类型下重复键值
        var dupValue = await SendAsync(HttpMethod.Post, "/api/system/dict-data", new
        {
            dictType,
            dictLabel = "选项C3",
            dictValue = "C",
            tagType = 1,
            orderNum = 1,
            status = 1
        });
        var dupValueBody = await ReadBodyAsync(dupValue);

        // 不存在的字典类型
        var missingType = await SendAsync(HttpMethod.Post, "/api/system/dict-data", new
        {
            dictType = dictType + "missing",
            dictLabel = "x",
            dictValue = "x",
            tagType = 1,
            orderNum = 1,
            status = 1
        });
        var missingTypeBody = await ReadBodyAsync(missingType);

        Assert.Multiple(() =>
        {
            Assert.That(dupLabel.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(dupLabelBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400606));
            Assert.That(dupValue.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(dupValueBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400606));
            Assert.That(missingType.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(missingTypeBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400601));
        });

        await SendAsync(HttpMethod.Delete, $"/api/system/dict-type/{typeId}/{await ApiTestSupport.GetVersionAsync<SysDictType>(_db, typeId)}");
    }

    // —— 参数配置 ——

    /// <summary>验证参数配置全流程：创建、按键取值缓存刷新、重复键拒绝、内置参数受保护。</summary>
    [Test]
    public async Task Config_FullFlow_KeyCacheRefreshedAndBuiltInProtected()
    {
        var configKey = "b7.test." + Guid.NewGuid().ToString("N")[..10];

        // 创建（非内置）
        var createBody = await SendForJsonAsync(HttpMethod.Post, "/api/system/config", new
        {
            configName = "B7 测试参数",
            configKey,
            configValue = "v1",
            description = "B7 集成测试"
        });
        var configId = await ApiTestSupport.GetEntityIdAsync<SysConfig>(_db, it => it.ConfigKey == configKey);

        var page = await GetJsonAsync($"/api/system/config/page?keyword={configKey}");
        var row = page.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("id").GetGuid(), Is.EqualTo(configId));
            Assert.That(row.GetProperty("builtIn").GetBoolean(), Is.False, "创建项应为非内置");
            Assert.That(row.TryGetProperty("createdAt", out _), Is.True);
        });

        // 重复键名 → 400702
        var duplicate = await SendAsync(HttpMethod.Post, "/api/system/config", new
        {
            configName = "重复参数",
            configKey,
            configValue = "x"
        });
        var duplicateBody = await ReadBodyAsync(duplicate);
        Assert.Multiple(() =>
        {
            Assert.That(duplicate.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(duplicateBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400702));
        });

        // 按键取值（写入缓存）
        var first = await GetJsonAsync($"/api/system/config/key/{configKey}");
        Assert.That(first.RootElement.GetProperty("data").GetString(), Is.EqualTo("v1"));

        // 更新后立即可见（缓存已失效；乐观锁：先回读版本号）
        var configVersion = await ApiTestSupport.GetVersionAsync<SysConfig>(_db, configId);
        await SendForJsonAsync(HttpMethod.Put, $"/api/system/config/{configId}", new
        {
            version = configVersion,
            configName = "B7 测试参数",
            configKey,
            configValue = "v2",
            description = (string?)null
        });
        var afterUpdate = await GetJsonAsync($"/api/system/config/key/{configKey}");
        Assert.That(afterUpdate.RootElement.GetProperty("data").GetString(), Is.EqualTo("v2"), "更新后按键取值应立即返回新值");

        // 非内置可删除；删除后按键取值报不存在
        await SendAsync(HttpMethod.Delete, $"/api/system/config/{configId}/{await ApiTestSupport.GetVersionAsync<SysConfig>(_db, configId)}");
        var missing = await SendAsync(HttpMethod.Get, $"/api/system/config/key/{configKey}", null);
        var missingBody = await ReadBodyAsync(missing);
        Assert.Multiple(() =>
        {
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(missingBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400701));
        });
    }

    /// <summary>验证内置参数随种子数据初始化且禁止删除。</summary>
    [Test]
    public async Task Config_BuiltIn_SeededAndDeleteRejected()
    {
        // 种子参数按键取值
        var initPassword = await GetJsonAsync("/api/system/config/key/sys.user.initPassword");
        Assert.That(initPassword.RootElement.GetProperty("data").GetString(), Is.EqualTo("123456"), "种子参数 sys.user.initPassword 应为 123456");

        // 种子内置参数删除 → 400703
        var page = await GetJsonAsync("/api/system/config/page?keyword=sys.user.initPassword");
        var builtinId = page.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("id").GetGuid();
        var delete = await SendAsync(HttpMethod.Delete, $"/api/system/config/{builtinId}/{await ApiTestSupport.GetVersionAsync<SysConfig>(_db, builtinId)}");
        var deleteBody = await ReadBodyAsync(delete);
        Assert.Multiple(() =>
        {
            Assert.That(page.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("builtIn").GetBoolean(),
                Is.True, "种子参数应为内置");
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(deleteBody.RootElement.GetProperty("code").GetInt32(), Is.EqualTo(400703));
        });

        // 验证码开关种子参数存在
        var captchaEnabled = await GetJsonAsync("/api/system/config/key/sys.account.captchaEnabled");
        Assert.That(captchaEnabled.RootElement.GetProperty("data").GetString(), Is.EqualTo("true"));
    }

    // —— 测试辅助 ——

    /// <summary>生成唯一字典类型编码。</summary>
    private static string NewDictType() => "b7_dict_" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>以 admin 创建一条启用的字典数据，返回数据 Id。</summary>
    private async Task<Guid> CreateDictDataAsync(string dictType, string label, string value)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/system/dict-data", new
        {
            dictType,
            dictLabel = label,
            dictValue = value,
            tagType = 1,
            orderNum = 1,
            status = 1
        });
        var body = await ReadBodyAsync(response);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"创建字典数据应成功：{body.RootElement.GetProperty("message").GetString()}");
        return await ApiTestSupport.GetEntityIdAsync<SysDictData>(_db, it => it.DictType == dictType && it.DictValue == value);
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
