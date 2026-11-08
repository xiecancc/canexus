
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure.Tests;
/// <summary>
/// 配置选项验证单元测试：覆盖三个 Option 的合法默认值与各类非法取值（空值 / 越界 / 业务不变量），
/// 保证 Options 管道校验（启动 ValidateOnStart 与热加载重校验）能可靠拦截坏配置。
/// </summary>
[TestFixture]
public class OptionValidationTests
{
    // —— RegisterOption 管道（错误消息透传 + 具体类型注入） ——

    /// <summary>验证非法配置注册选项时抛出的异常保留字段级错误消息。</summary>
    [Test]
    public void RegisterOption_InvalidConfig_OptionsValidationExceptionKeepsFieldMessage()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = "short" })
            .Build();

        var services = new ServiceCollection();
        services.RegisterOption<JwtOption>(configuration, "Jwt");

        // 管道包装后的错误消息应保留字段级定位（来自 OptionBase.Validate 的 ArgumentException.Message）
        var ex = Assert.Throws<OptionsValidationException>(() =>
        {
            _ = services.BuildServiceProvider().GetRequiredService<IOptions<JwtOption>>().Value;
        });
        Assert.That(ex!.Message, Does.Contain("Jwt:Secret 按 UTF-8 编码后不得少于 32 字节"));
    }

    /// <summary>验证合法配置注册选项后具体类型可注入且与 IOptions 为同一实例。</summary>
    [Test]
    public void RegisterOption_ValidConfig_ConcreteTypeInjectableAndSameAsIOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "valid-secret-key-0123456789-abcdef"
            })
            .Build();

        var services = new ServiceCollection();
        services.RegisterOption<JwtOption>(configuration, "Jwt");

        using var provider = services.BuildServiceProvider();
        var fromConcrete = provider.GetRequiredService<JwtOption>();
        var fromIOptions = provider.GetRequiredService<IOptions<JwtOption>>().Value;

        // RegisterOption 封装的具体类型注册与 IOptions 解析到同一托管实例
        Assert.That(fromConcrete, Is.SameAs(fromIOptions));
        Assert.DoesNotThrow(() => fromConcrete.Validate());
    }

    // —— CacheOption ——

    /// <summary>验证缓存选项默认值通过校验。</summary>
    [Test]
    public void CacheOption_Defaults_Pass()
    {
        Assert.DoesNotThrow(() => new CacheOption().Validate());
    }

    /// <summary>验证缓存 TTL 为零时校验抛出异常。</summary>
    [Test]
    public void CacheOption_ZeroTtl_Throws()
    {
        var option = new CacheOption();
        option.Hot.LocalSeconds = 0;

        Assert.Throws<ArgumentOutOfRangeException>(() => option.Validate());
    }

    /// <summary>验证缓存 TTL 超过一天时校验抛出异常。</summary>
    [Test]
    public void CacheOption_TtlExceedsOneDay_Throws()
    {
        var option = new CacheOption();
        option.Cold.DistributedSeconds = 86_401;

        Assert.Throws<ArgumentOutOfRangeException>(() => option.Validate());
    }

    /// <summary>验证本地缓存时长长于分布式缓存时长时校验抛出异常。</summary>
    [Test]
    public void CacheOption_LocalLongerThanDistributed_Throws()
    {
        var option = new CacheOption();
        option.Medium.LocalSeconds = 700;   // L1 不应长于 L2（默认 DistributedSeconds = 600）

        var ex = Assert.Throws<ArgumentException>(() => option.Validate());
        Assert.That(ex!.Message, Does.Contain("不应长于"));
    }

    /// <summary>验证未启用 Redis 且连接配置为空时校验通过。</summary>
    [Test]
    public void CacheOption_RedisDisabled_EmptyRedisConfiguration_Passes()
    {
        var option = new CacheOption { RedisEnabled = false, RedisConfiguration = string.Empty };

        Assert.DoesNotThrow(() => option.Validate());
    }

    /// <summary>验证启用 Redis 但连接配置为空时校验抛出异常。</summary>
    [Test]
    public void CacheOption_RedisEnabled_EmptyRedisConfiguration_Throws()
    {
        var option = new CacheOption { RedisEnabled = true, RedisConfiguration = " " };

        Assert.Throws<ArgumentException>(() => option.Validate());
    }

    /// <summary>验证最大负载字节数为零时校验抛出异常。</summary>
    [Test]
    public void CacheOption_ZeroMaximumPayloadBytes_Throws()
    {
        var option = new CacheOption { MaximumPayloadBytes = 0 };

        Assert.Throws<ArgumentException>(() => option.Validate());
    }

    // —— DatabaseOption ——

    /// <summary>验证数据库连接字符串为空时校验抛出异常。</summary>
    [Test]
    public void DatabaseOption_EmptyConnectionString_Throws()
    {
        var option = new DatabaseOption { ConnectionString = string.Empty };

        var ex = Assert.Throws<ArgumentException>(() => option.Validate());
        Assert.That(ex!.Message, Does.Contain("ConnectionString 不能为空"));
    }

    /// <summary>验证数据库选项默认值通过校验。</summary>
    [Test]
    public void DatabaseOption_Defaults_Pass()
    {
        var option = new DatabaseOption { ConnectionString = "server=localhost;database=test" };

        Assert.DoesNotThrow(() => option.Validate());
    }

    /// <summary>验证命令超时时间超出范围时校验抛出异常。</summary>
    [Test]
    public void DatabaseOption_CommandTimeOutOutOfRange_Throws()
    {
        var option = new DatabaseOption { ConnectionString = "server=localhost;database=test", CommandTimeOut = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(() => option.Validate());
    }

    /// <summary>验证数据库类型取值非法时校验抛出异常。</summary>
    [Test]
    public void DatabaseOption_InvalidDbType_Throws()
    {
        var option = new DatabaseOption
        {
            ConnectionString = "server=localhost;database=test",
            DbType = (SqlSugar.DbType)999
        };

        Assert.Throws<ArgumentException>(() => option.Validate());
    }

    // —— JwtOption ——

    /// <summary>验证 JWT 配置合法时通过校验。</summary>
    [Test]
    public void JwtOption_ValidConfig_Passes()
    {
        var option = new JwtOption { Secret = "valid-secret-key-0123456789-abcdef" };

        Assert.DoesNotThrow(() => option.Validate());
    }

    /// <summary>验证 JWT 密钥过短时校验抛出异常。</summary>
    [Test]
    public void JwtOption_ShortSecret_Throws()
    {
        var option = new JwtOption { Secret = "short" };

        var ex = Assert.Throws<ArgumentException>(() => option.Validate());
        Assert.That(ex!.Message, Does.Contain("不得少于 32 字节"));
    }

    /// <summary>验证 JWT 密钥仍为模板默认不安全值时校验抛出异常（S3 启动强校验）。</summary>
    [Test]
    public void JwtOption_InsecureDefaultSecret_Throws()
    {
        var option = new JwtOption { Secret = JwtOption.InsecureDefaultSecret };

        var ex = Assert.Throws<ArgumentException>(() => option.Validate());
        Assert.That(ex!.Message, Does.Contain("默认不安全值"));
    }

    /// <summary>验证 accessToken 有效期超过短时令牌窗口（30 分钟）时校验抛出异常。</summary>
    [Test]
    public void JwtOption_AccessTokenMinutesExceedsWindow_Throws()
    {
        var option = new JwtOption
        {
            Secret = "valid-secret-key-0123456789-abcdef",
            AccessTokenMinutes = 31
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => option.Validate());
    }

    /// <summary>验证刷新令牌有效期超过一年时校验抛出异常。</summary>
    [Test]
    public void JwtOption_RefreshTokenDaysExceedsOneYear_Throws()
    {
        var option = new JwtOption
        {
            Secret = "valid-secret-key-0123456789-abcdef",
            RefreshTokenDays = 366
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => option.Validate());
    }

    /// <summary>验证 JWT 签发者为空时校验抛出异常。</summary>
    [Test]
    public void JwtOption_EmptyIssuer_Throws()
    {
        var option = new JwtOption
        {
            Issuer = string.Empty,
            Secret = "valid-secret-key-0123456789-abcdef"
        };

        Assert.Throws<ArgumentException>(() => option.Validate());
    }

    // —— CorsOption ——

    /// <summary>验证未启用 CORS 且允许来源为空时校验通过。</summary>
    [Test]
    public void CorsOption_Disabled_EmptyOrigins_Passes()
    {
        var option = new CorsOption { Enabled = false, AllowedOrigins = [] };

        Assert.DoesNotThrow(() => option.Validate());
    }

    /// <summary>验证启用 CORS 但允许来源为空时校验抛出异常。</summary>
    [Test]
    public void CorsOption_Enabled_EmptyOrigins_Throws()
    {
        var option = new CorsOption { Enabled = true, AllowedOrigins = [] };

        var ex = Assert.Throws<ArgumentException>(() => option.Validate());
        Assert.That(ex!.Message, Does.Contain("AllowedOrigins 不能为空"));
    }

    /// <summary>验证启用 CORS 且来源含通配符时校验抛出异常。</summary>
    [Test]
    public void CorsOption_Enabled_WildcardOrigin_Throws()
    {
        var option = new CorsOption { Enabled = true, AllowedOrigins = ["*"] };

        var ex = Assert.Throws<ArgumentException>(() => option.Validate());
        Assert.That(ex!.Message, Does.Contain("不允许包含"));
    }

    /// <summary>验证启用 CORS 且来源合法时通过校验。</summary>
    [Test]
    public void CorsOption_Enabled_ValidOrigins_Passes()
    {
        var option = new CorsOption { Enabled = true, AllowedOrigins = ["http://localhost:5173"] };

        Assert.DoesNotThrow(() => option.Validate());
    }

    /// <summary>验证预检请求最大缓存时长为零时校验抛出异常。</summary>
    [Test]
    public void CorsOption_ZeroPreflightMaxAge_Throws()
    {
        var option = new CorsOption { Enabled = true, AllowedOrigins = ["http://localhost:5173"], PreflightMaxAgeSeconds = 0 };

        Assert.Throws<ArgumentException>(() => option.Validate());
    }

    // —— RateLimitOption ——

    /// <summary>验证限流选项默认值通过校验。</summary>
    [Test]
    public void RateLimitOption_Defaults_Pass()
    {
        Assert.DoesNotThrow(() => new RateLimitOption().Validate());
    }

    /// <summary>验证启用限流但登录接口允许次数为零时校验抛出异常。</summary>
    [Test]
    public void RateLimitOption_Enabled_ZeroAuthPermitLimit_Throws()
    {
        var option = new RateLimitOption { Enabled = true, AuthPermitLimit = 0 };

        Assert.Throws<ArgumentException>(() => option.Validate());
    }

    /// <summary>验证启用限流但全局限流次数为负时校验抛出异常。</summary>
    [Test]
    public void RateLimitOption_Enabled_NegativeGlobalPermitLimit_Throws()
    {
        var option = new RateLimitOption { Enabled = true, GlobalPermitLimit = -1 };

        Assert.Throws<ArgumentException>(() => option.Validate());
    }

    /// <summary>验证全局限流窗口时长为零时校验抛出异常。</summary>
    [Test]
    public void RateLimitOption_GlobalLimitWithZeroWindow_Throws()
    {
        var option = new RateLimitOption { Enabled = true, GlobalPermitLimit = 100, GlobalWindowSeconds = 0 };

        Assert.Throws<ArgumentException>(() => option.Validate());
    }

    /// <summary>验证未启用限流时忽略非法取值并通过校验。</summary>
    [Test]
    public void RateLimitOption_Disabled_IgnoresInvalidValues_Passes()
    {
        var option = new RateLimitOption { Enabled = false, AuthPermitLimit = 0, AuthWindowSeconds = 0 };

        Assert.DoesNotThrow(() => option.Validate());
    }

    // —— HttpsOption ——

    /// <summary>验证 HTTPS 选项默认值通过校验。</summary>
    [Test]
    public void HttpsOption_Defaults_Pass()
    {
        Assert.DoesNotThrow(() => new HttpsOption().Validate());
    }

    /// <summary>验证 HTTPS 端口号超出范围时校验抛出异常。</summary>
    [Test]
    public void HttpsOption_InvalidPort_Throws()
    {
        var option = new HttpsOption { HttpsPort = 70_000 };

        Assert.Throws<ArgumentException>(() => option.Validate());
    }

    /// <summary>验证 HSTS 最大有效期超出范围时校验抛出异常。</summary>
    [Test]
    public void HttpsOption_HstsMaxAgeOutOfRange_Throws()
    {
        var option = new HttpsOption { HstsMaxAgeDays = 731 };

        Assert.Throws<ArgumentException>(() => option.Validate());
    }
}
