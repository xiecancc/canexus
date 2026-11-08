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
