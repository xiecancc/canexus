using API.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace API.Tests;

/// <summary>T-13：Production 配置基线禁止应用自动改表（结构变更须走迁移通道）。</summary>
[TestFixture]
public class ProductionConfigurationValidatorTests
{
    /// <summary>Production 下 AutoCreateTable / AutoSyncStructure 未显式关闭时，失败消息列出这两项。</summary>
    [Test]
    public void ProductionBaseline_RequiresAutoDdlDisabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:AutoCreateTable"] = "true",
                ["Database:AutoSyncStructure"] = "true"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("Database:AutoCreateTable"));
            Assert.That(exception.Message, Does.Contain("Database:AutoSyncStructure"));
        });
    }

    /// <summary>显式关闭两项后，这两条规则不再出现在失败清单中。</summary>
    [Test]
    public void ProductionBaseline_AcceptsExplicitlyDisabledDdl()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:AutoCreateTable"] = "false",
                ["Database:AutoSyncStructure"] = "false"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Not.Contain("Database:AutoCreateTable"));
            Assert.That(exception.Message, Does.Not.Contain("Database:AutoSyncStructure"));
        });
    }

    /// <summary>回归：Production 缺失内置超管初始口令时启动被拒绝（种子口令随机化基线）。</summary>
    [Test]
    public void ProductionBaseline_RequiresExplicitAdminInitialPassword()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "Prod-Only@Random#Secret-2026-abcdefghijklmnopqrstuvwxyz",
                ["Bootstrap:AdminInitialPassword"] = ""
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.That(exception!.Message, Does.Contain("Bootstrap:AdminInitialPassword 必须显式提供"));
    }

    /// <summary>回归：Production 不得关闭「种子 admin 首登须改密」。</summary>
    [Test]
    public void ProductionBaseline_RejectsDisabledPasswordChangeOnSeed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "Prod-Only@Random#Secret-2026-abcdefghijklmnopqrstuvwxyz",
                ["Bootstrap:AdminInitialPassword"] = "Prod-Only@Initial#Password-2026",
                ["Bootstrap:RequirePasswordChangeOnSeed"] = "false"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.That(exception!.Message, Does.Contain("Bootstrap:RequirePasswordChangeOnSeed 在 Production 必须为 true"));
    }

    /// <summary>回归：Production 必须启用 Redis L2，否则会话吊销 / 失效广播 / 分布式限流无跨实例统一视图。</summary>
    [Test]
    public void ProductionBaseline_RequiresRedisEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RedisEnabled"] = "false"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.That(exception!.Message, Does.Contain("Cache:RedisEnabled 在 Production 必须为 true"));
    }

    /// <summary>回归：显式开启 Redis L2 缓存后，该基线项不再出现在失败清单中。</summary>
    [Test]
    public void ProductionBaseline_AcceptsRedisEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RedisEnabled"] = "true",
                ["RateLimit:UseRedis"] = "true",
                ["Jwt:Secret"] = "Prod-Only@Random#Secret-2026-abcdefghijklmnopqrstuvwxyz",
                ["Bootstrap:AdminInitialPassword"] = "Prod-Only@Initial#Password-2026"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.That(exception!.Message, Does.Not.Contain("Cache:RedisEnabled"));
    }

    /// <summary>回归：Production 启用 Redis 缓存但未开启分布式限流时，仍会因多副本配额放大而阻止启动。</summary>
    [Test]
    public void ProductionBaseline_RequiresDistributedRateLimitEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RedisEnabled"] = "true",
                ["RateLimit:UseRedis"] = "false"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.That(exception!.Message, Does.Contain("RateLimit:UseRedis 在 Production 必须为 true"));
    }

    /// <summary>回归：Production 未配置 RateLimit:UseRedis（默认 false）同样被拦截，不允许依赖隐式默认值。</summary>
    [Test]
    public void ProductionBaseline_RejectsMissingDistributedRateLimitSwitch()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RedisEnabled"] = "true"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.That(exception!.Message, Does.Contain("RateLimit:UseRedis 在 Production 必须为 true"));
    }

    /// <summary>回归：两个 Redis 开关同时显式开启后，该基线项不再出现在失败清单中。</summary>
    [Test]
    public void ProductionBaseline_AcceptsBothRedisSwitchesEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RedisEnabled"] = "true",
                ["RateLimit:UseRedis"] = "true",
                ["Jwt:Secret"] = "Prod-Only@Random#Secret-2026-abcdefghijklmnopqrstuvwxyz",
                ["Bootstrap:AdminInitialPassword"] = "Prod-Only@Initial#Password-2026"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Not.Contain("Cache:RedisEnabled"));
            Assert.That(exception.Message, Does.Not.Contain("RateLimit:UseRedis"));
        });
    }

    /// <summary>P1-8：未显式配置 RequirePasswordChangeOnSeed（appsettings.json 不含该键）时 Production 启动被拒绝。</summary>
    [Test]
    public void ProductionBaseline_RejectsMissingPasswordChangeOnSeed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "Prod-Only@Random#Secret-2026-abcdefghijklmnopqrstuvwxyz",
                ["Bootstrap:AdminInitialPassword"] = "Prod-Only@Initial#Password-2026"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.That(exception!.Message, Does.Contain("Bootstrap:RequirePasswordChangeOnSeed 在 Production 必须为 true"));
    }

    /// <summary>P1-8：RequirePasswordChangeOnSeed 显式为 true 且其余基线全部合规时，Validate 不抛异常。</summary>
    [Test]
    public void ProductionBaseline_AcceptsExplicitlyEnabledPasswordChangeOnSeed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "Prod-Only@Random#Secret-2026-abcdefghijklmnopqrstuvwxyz",
                ["Bootstrap:AdminInitialPassword"] = "Prod-Only@Initial#Password-2026",
                ["Bootstrap:RequirePasswordChangeOnSeed"] = "true",
                ["Database:AutoCreateTable"] = "false",
                ["Database:AutoSyncStructure"] = "false",
                ["Database:AllowSchemaInitialization"] = "false",
                ["Database:MigrationsEnabled"] = "true",
                ["Database:ValidateModelStructure"] = "true",
                ["Https:RedirectEnabled"] = "true",
                ["Https:HstsEnabled"] = "true",
                ["RateLimit:Enabled"] = "true",
                ["RateLimit:GlobalPermitLimit"] = "100",
                ["MultiTenancy:Enabled"] = "false",
                ["Cache:RedisEnabled"] = "true",
                ["RateLimit:UseRedis"] = "true"
            })
            .Build();

        Assert.DoesNotThrow(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
    }

    /// <summary>P1-8：未开启 ValidateModelStructure 时 Production 启动被拒绝。</summary>
    [Test]
    public void ProductionBaseline_RequiresValidateModelStructureEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ValidateModelStructure"] = "false"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.That(exception!.Message, Does.Contain("Database:ValidateModelStructure 在 Production 必须为 true"));
    }

    /// <summary>P1-8：AllowSchemaInitialization 稳态保留 true 时 Production 启动被拒绝（仅一次性初始化可临时开启）。</summary>
    [Test]
    public void ProductionBaseline_RejectsAllowSchemaInitializationEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:AllowSchemaInitialization"] = "true"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.That(exception!.Message, Does.Contain("Database:AllowSchemaInitialization 在 Production 稳态必须为 false"));
    }

    /// <summary>P1-8：显式关闭迁移通道时 Production 启动被拒绝。</summary>
    [Test]
    public void ProductionBaseline_RejectsDisabledMigrations()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:MigrationsEnabled"] = "false"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.That(exception!.Message, Does.Contain("Database:MigrationsEnabled 在 Production 必须为 true"));
    }

    /// <summary>测试用宿主环境桩。</summary>
    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        /// <inheritdoc />
        public string EnvironmentName { get; set; } = name;

        /// <inheritdoc />
        public string ApplicationName { get; set; } = "API.Tests";

        /// <inheritdoc />
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        /// <inheritdoc />
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
