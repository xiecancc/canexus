namespace Infrastructure.Tests;

using Infrastructure.Shared.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// 配置选项验证单元测试：覆盖三个 Option 的合法默认值与各类非法取值（空值 / 越界 / 业务不变量），
/// 保证 Options 管道校验（启动 ValidateOnStart 与热加载重校验）能可靠拦截坏配置。
/// </summary>
[TestFixture]
public class OptionValidationTests
{
    // —— RegisterOption 管道（错误消息透传 + 具体类型注入） ——

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
        Assert.That(ex!.Message, Does.Contain("Jwt:Secret 长度不得小于 32 字符"));
    }

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

    [Test]
    public void CacheOption_Defaults_Pass()
    {
        Assert.DoesNotThrow(() => new CacheOption().Validate());
    }

    [Test]
    public void CacheOption_ZeroTtl_Throws()
    {
        var option = new CacheOption();
        option.Hot.LocalSeconds = 0;

        Assert.Throws<ArgumentOutOfRangeException>(() => option.Validate());
    }

    [Test]
    public void CacheOption_TtlExceedsOneDay_Throws()
    {
        var option = new CacheOption();
        option.Cold.DistributedSeconds = 86_401;

        Assert.Throws<ArgumentOutOfRangeException>(() => option.Validate());
    }

    [Test]
    public void CacheOption_LocalLongerThanDistributed_Throws()
    {
        var option = new CacheOption();
        option.Medium.LocalSeconds = 700;   // L1 不应长于 L2（默认 DistributedSeconds = 600）

        var ex = Assert.Throws<ArgumentException>(() => option.Validate());
        Assert.That(ex!.Message, Does.Contain("不应长于"));
    }

    [Test]
    public void CacheOption_RedisDisabled_EmptyRedisConfiguration_Passes()
    {
        var option = new CacheOption { RedisEnabled = false, RedisConfiguration = string.Empty };

        Assert.DoesNotThrow(() => option.Validate());
    }

    [Test]
    public void CacheOption_RedisEnabled_EmptyRedisConfiguration_Throws()
    {
        var option = new CacheOption { RedisEnabled = true, RedisConfiguration = " " };

        Assert.Throws<ArgumentException>(() => option.Validate());
    }

    [Test]
    public void CacheOption_ZeroMaximumPayloadBytes_Throws()
    {
        var option = new CacheOption { MaximumPayloadBytes = 0 };

        Assert.Throws<ArgumentException>(() => option.Validate());
    }

    // —— DatabaseOption ——

    [Test]
    public void DatabaseOption_EmptyConnectionString_Throws()
    {
        var option = new DatabaseOption { ConnectionString = string.Empty };

        var ex = Assert.Throws<ArgumentException>(() => option.Validate());
        Assert.That(ex!.Message, Does.Contain("ConnectionString 不能为空"));
    }

    [Test]
    public void DatabaseOption_Defaults_Pass()
    {
        var option = new DatabaseOption { ConnectionString = "server=localhost;database=test" };

        Assert.DoesNotThrow(() => option.Validate());
    }

    [Test]
    public void DatabaseOption_CommandTimeOutOutOfRange_Throws()
    {
        var option = new DatabaseOption { ConnectionString = "server=localhost;database=test", CommandTimeOut = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(() => option.Validate());
    }

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

    [Test]
    public void JwtOption_ValidConfig_Passes()
    {
        var option = new JwtOption { Secret = "valid-secret-key-0123456789-abcdef" };

        Assert.DoesNotThrow(() => option.Validate());
    }

    [Test]
    public void JwtOption_ShortSecret_Throws()
    {
        var option = new JwtOption { Secret = "short" };

        var ex = Assert.Throws<ArgumentException>(() => option.Validate());
        Assert.That(ex!.Message, Does.Contain("不得小于 32 字符"));
    }

    [Test]
    public void JwtOption_AccessTokenMinutesExceedsOneDay_Throws()
    {
        var option = new JwtOption
        {
            Secret = "valid-secret-key-0123456789-abcdef",
            AccessTokenMinutes = 1_441
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => option.Validate());
    }

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
}
