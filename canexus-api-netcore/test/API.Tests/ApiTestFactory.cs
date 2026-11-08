
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.Tests;
/// <summary>
/// 统一集成测试宿主工厂：固定 Testing 环境、注入测试专用的引导配置、注册测试程序集内的探针控制器，
/// 并在宿主创建后断言最终生效的连接串指向隔离测试库（不写开发库 super_admin）。
/// <para>各测试类仍可经 <c>WithWebHostBuilder</c> 追加自身专属配置（如独立数据库名、限流参数）。</para>
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 显式固定环境：既不触发 Development 的弱密钥回退，也不触发 Production 部署基线校验
        builder.UseEnvironment("Testing");

        // 复用既有集成测试凭据：测试库内 admin 保持固定口令且不要求首登改密；
        // 生产由 ProductionConfigurationValidator 禁止关闭 RequirePasswordChangeOnSeed
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bootstrap:AdminInitialPassword"] = ApiTestSupport.TestAdminPassword,
                ["Bootstrap:RequirePasswordChangeOnSeed"] = "false"
            }));

        // 测试专用探针端点只存在于测试程序集，经 ApplicationPart 注册后仍走生产同一套 MVC 过滤器
        builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(ApiTestFactory).Assembly));
    }

    /// <inheritdoc />
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        var connectionString = host.Services.GetRequiredService<IConfiguration>()["Database:ConnectionString"];
        Assert.That(connectionString, Is.Not.Null.And.Not.Empty, "测试宿主必须注入隔离测试库连接串");

        var match = Regex.Match(connectionString!, @"(?:^|;)\s*database\s*=\s*([^;]+)", RegexOptions.IgnoreCase);
        Assert.That(match.Success, Is.True, "测试连接串须显式声明 database=...");
        Assert.That(match.Groups[1].Value.Trim(), Is.Not.EqualTo("super_admin"),
            "连接串覆盖失效：测试宿主不得写入开发库 super_admin");
        return host;
    }
}
