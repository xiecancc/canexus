using API.Authorization;
using API.Filters;
using Application.Permissions;
using Domain.Services.Users;
using Domain.Shared.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace API.Tests;
/// <summary>
/// 权限默认拒绝（P0）测试：
/// 启动期反向扫描（API 程序集全量合规 + 测试程序集违规端点检出）与
/// 运行期过滤器兜底（无权限声明且无豁免的端点 403）。
/// </summary>
[TestFixture]
public class PermissionDefaultDenyTests
{
    // —— 启动期反向扫描 ——

    /// <summary>API 程序集全部端点均已显式声明访问策略（含 AuthController 四个登录即可端点的豁免标注）。</summary>
    [Test]
    public void ReverseScan_ApiAssembly_NoViolations()
    {
        var violations = PermissionsStartupValidator.FindUnauthorizedEndpoints(typeof(Program).Assembly);
        Assert.That(violations, Is.Empty, $"API 程序集应无权限声明违规：{string.Join("; ", violations)}");
    }

    /// <summary>反向扫描能检出受保护但未声明权限的端点（测试程序集内的违规控制器）。</summary>
    [Test]
    public void ReverseScan_RogueController_Detected()
    {
        var violations = PermissionsStartupValidator.FindUnauthorizedEndpoints(typeof(PermissionDefaultDenyTests).Assembly);

        Assert.That(violations, Has.Some.Contains(nameof(RogueProtectedController)),
            "受保护端点漏写 [RequirePermission] 应被检出");
    }

    /// <summary>反向扫描不应误报合规端点（匿名 / 权限声明 / 登录即可豁免 + Authorize 组合均放行）。</summary>
    [Test]
    public void ReverseScan_CompliantControllers_NotReported()
    {
        var violations = PermissionsStartupValidator.FindUnauthorizedEndpoints(typeof(PermissionDefaultDenyTests).Assembly);

        Assert.Multiple(() =>
        {
            Assert.That(violations, Has.None.Contains(nameof(CompliantPermissionController)), "[RequirePermission] 端点应放行");
            Assert.That(violations, Has.None.Contains(nameof(CompliantAnonymousController)), "[AllowAnonymous] 端点应放行");
            Assert.That(violations, Has.None.Contains(nameof(CompliantAnyAuthenticatedController)), "[AllowAnyAuthenticated]+[Authorize] 端点应放行");
        });
    }

    // —— 运行期过滤器兜底 ——

    /// <summary>无权限声明且无豁免的端点被过滤器默认拒绝（403），即漏写声明不会退化为登录即可访问。</summary>
    [Test]
    public void Filter_UndeclaredEndpoint_DefaultDeny403()
    {
        var filter = CreateFilter();
        var context = CreateFilterContext(metadata: []);

        filter.OnAuthorizationAsync(context).GetAwaiter().GetResult();

        Assert.That(context.Result, Is.Not.Null, "默认拒绝应短路请求");
        Assert.That((context.Result as JsonResult)?.StatusCode, Is.EqualTo(403));
    }

    /// <summary>[AllowAnyAuthenticated] 端点：未认证 401，已认证放行（不校验细粒度权限）。</summary>
    [Test]
    public async Task Filter_AnyAuthenticated_Unauthenticated401_AuthenticatedPass()
    {
        var unauthenticated = CreateFilterContext(metadata: [new AllowAnyAuthenticatedAttribute()]);
        await CreateFilter(authenticated: false).OnAuthorizationAsync(unauthenticated);
        Assert.That((unauthenticated.Result as JsonResult)?.StatusCode, Is.EqualTo(401), "未认证应返回 401");

        var authenticated = CreateFilterContext(metadata: [new AllowAnyAuthenticatedAttribute()]);
        await CreateFilter(authenticated: true).OnAuthorizationAsync(authenticated);
        Assert.That(authenticated.Result, Is.Null, "已认证应放行且不做权限校验");
    }

    /// <summary>[AllowAnonymous] 端点直接放行。</summary>
    [Test]
    public async Task Filter_Anonymous_Passes()
    {
        var filter = CreateFilter();
        var context = CreateFilterContext(metadata: [new AllowAnonymousAttribute()]);

        await filter.OnAuthorizationAsync(context);

        Assert.That(context.Result, Is.Null, "匿名端点应放行");
    }

    // —— 违规控制器样例（仅供反向扫描断言，不注册进 MVC）——

    /// <summary>违规样例：端点未声明任何访问策略（反向扫描应检出）。</summary>
    public class RogueProtectedController : ControllerBase
    {
        /// <summary>未声明任何访问策略的端点。</summary>
        [HttpGet("rogue")]
        public IActionResult Leak() => Ok();
    }

    /// <summary>合规样例：带 [RequirePermission]（权限标识在注册表中声明）。</summary>
    public class CompliantPermissionController : ControllerBase
    {
        /// <summary>权限声明的端点。</summary>
        [HttpGet("compliant-perm")]
        [RequirePermission(PermissionDefinitions.SystemUserList)]
        public IActionResult Action1() => Ok();
    }

    /// <summary>合规样例：匿名端点。</summary>
    public class CompliantAnonymousController : ControllerBase
    {
        /// <summary>匿名端点。</summary>
        [HttpGet("compliant-anon")]
        [AllowAnonymous]
        public IActionResult Action1() => Ok();
    }

    /// <summary>合规样例：登录即可豁免 + 授权语义。</summary>
    public class CompliantAnyAuthenticatedController : ControllerBase
    {
        /// <summary>登录即可端点。</summary>
        [HttpGet("compliant-any-auth")]
        [Authorize]
        [AllowAnyAuthenticated]
        public IActionResult Action1() => Ok();
    }

    // —— 测试辅助 ——

    /// <summary>构造过滤器（权限校验器桩：默认拒绝与豁免路径不应触达细粒度校验，触达即失败）。</summary>
    private static PermissionAuthorizationFilter CreateFilter(bool authenticated = false) =>
        new(new ThrowingPermissionChecker(), new StubUserContext(authenticated), NullLogger<PermissionAuthorizationFilter>.Instance);

    /// <summary>构造过滤器执行上下文（带端点元数据）。</summary>
    private static AuthorizationFilterContext CreateFilterContext(IReadOnlyList<object> metadata)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(metadata),
            "test-endpoint"));

        return new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            []);
    }

    /// <summary>权限校验器桩：任何细粒度校验调用都抛异常（默认拒绝 / 豁免路径不应触达）。</summary>
    private sealed class ThrowingPermissionChecker : IPermissionChecker
    {
        /// <inheritdoc />
        public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("默认拒绝路径不应触发细粒度权限校验");

        /// <inheritdoc />
        public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("默认拒绝路径不应触发细粒度权限校验");

        /// <inheritdoc />
        public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public Task InvalidateAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>当前用户上下文桩：认证态由构造参数控制。</summary>
    private sealed class StubUserContext(bool authenticated) : IUserContext
    {
        /// <inheritdoc />
        public Guid? UserId => authenticated ? Guid.NewGuid() : null;

        /// <inheritdoc />
        public string? UserName => authenticated ? "stub-user" : null;

        /// <inheritdoc />
        public Guid? DeptId => null;

        /// <inheritdoc />
        public IReadOnlyList<Guid> RoleIds => [];

        /// <inheritdoc />
        public IReadOnlyList<string> Roles => [];

        /// <inheritdoc />
        public Guid? TenantId => null;

        /// <inheritdoc />
        public bool IsAuthenticated => authenticated;

        /// <inheritdoc />
        public bool IsSuperAdmin => false;
    }
}
