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

    /// <summary>回归：显式开启 Redis 后该基线项不再出现在失败清单中。</summary>
    [Test]
    public void ProductionBaseline_AcceptsRedisEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RedisEnabled"] = "true",
                ["Jwt:Secret"] = "Prod-Only@Random#Secret-2026-abcdefghijklmnopqrstuvwxyz",
                ["Bootstrap:AdminInitialPassword"] = "Prod-Only@Initial#Password-2026"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, new StubEnvironment("Production")));
        Assert.That(exception!.Message, Does.Not.Contain("Cache:RedisEnabled"));
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
