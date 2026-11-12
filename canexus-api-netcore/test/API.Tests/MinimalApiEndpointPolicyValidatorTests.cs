
using API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace API.Tests;
/// <summary>启动期 minimal API 端点策略校验回归（评审 A3）：匿名探针放行，受保护的裸 minimal 端点被拦截。</summary>
[TestFixture]
public class MinimalApiEndpointPolicyValidatorTests
{
    /// <summary>全部非 MVC 端点显式匿名时校验通过（生产现状：健康检查 + OpenAPI 文档）。</summary>
    [Test]
    public void AllMinimalEndpointsAnonymous_Passes()
    {
        var endpoints = new TestEndpointDataSource(
            CreateEndpoint("GET /health/live", anonymous: true),
            CreateEndpoint("GET /health/ready", anonymous: true));

        Assert.That(MinimalApiEndpointPolicyValidator.FindNonAnonymousMinimalEndpoints(endpoints), Is.Empty);
        Assert.DoesNotThrow(() => MinimalApiEndpointPolicyValidator.Validate(endpoints));
    }

    /// <summary>新增未声明策略的 minimal API 端点时，启动期必须 fail-closed 阻止启动。</summary>
    [Test]
    public void ProtectedMinimalEndpoint_WithoutAnonymous_IsRejected()
    {
        var endpoints = new TestEndpointDataSource(CreateEndpoint("GET /reports/summary", anonymous: false));

        var exception = Assert.Throws<InvalidOperationException>(
            () => MinimalApiEndpointPolicyValidator.Validate(endpoints));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("minimal API 端点策略校验失败"));
            Assert.That(exception.Message, Does.Contain("GET /reports/summary"));
            Assert.That(exception.Message, Does.Contain("RequirePermission"));
        });
    }

    /// <summary>真实宿主下端点集合已就绪且全部合规，证明启动期校验不是空转。</summary>
    [Test]
    public void RealHost_MinimalEndpointsAreEnumerableAndAllAnonymous()
    {
        var apiContentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "API"));
        using var factory = new ApiTestFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddJsonFile(Path.Combine(apiContentRoot, "appsettings.json"), optional: false, reloadOnChange: false);
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = ApiTestSupport.TestConnectionString,
                    ["Jwt:Secret"] = ApiTestSupport.TestJwtSecret,
                    ["RateLimit:Enabled"] = "false"
                });
            }));
        _ = factory.CreateClient();

        var dataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        var minimalCount = dataSource.Endpoints.Count(
            endpoint => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>() is null);

        Assert.Multiple(() =>
        {
            Assert.That(minimalCount, Is.GreaterThan(0), "宿主应能枚举到健康检查等 minimal API 端点");
            Assert.That(MinimalApiEndpointPolicyValidator.FindNonAnonymousMinimalEndpoints(dataSource), Is.Empty);
        });
    }

    /// <summary>MVC 控制器端点由 PermissionsStartupValidator 扫描，本校验器应跳过。</summary>
    [Test]
    public void ControllerEndpoints_AreSkipped()
    {
        var builder = new TestEndpointDataSource(CreateEndpoint("POST /api/system/user", anonymous: false));
        // 模拟控制器端点元数据：ControllerActionDescriptor 存在即视为 MVC 端点
        var controllerEndpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()),
            "POST /api/system/user (MVC)");
        var endpoints = new TestEndpointDataSource(controllerEndpoint);

        Assert.That(MinimalApiEndpointPolicyValidator.FindNonAnonymousMinimalEndpoints(endpoints), Is.Empty);
    }

    /// <summary>构造带 / 不带匿名元数据的测试端点。</summary>
    /// <param name="displayName">端点显示名。</param>
    /// <param name="anonymous">是否显式匿名。</param>
    /// <returns>端点实例。</returns>
    private static Endpoint CreateEndpoint(string displayName, bool anonymous)
    {
        var metadata = anonymous
            ? new EndpointMetadataCollection(new AllowAnonymousAttribute())
            : new EndpointMetadataCollection();
        return new Endpoint(_ => Task.CompletedTask, metadata, displayName);
    }

    /// <summary>测试用端点集合。</summary>
    /// <param name="items">端点列表。</param>
    private sealed class TestEndpointDataSource(params Endpoint[] items) : EndpointDataSource
    {
        /// <inheritdoc />
        public override IReadOnlyList<Endpoint> Endpoints => items;

        /// <inheritdoc />
        public override IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
}
