
using Application.Behaviors;
using Application.Contracts.Abstractions;
using Application.Permissions;
using Domain.Services.Users;
using Domain.Shared.Exceptions;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.Behaviors;

/// <summary>
/// 命令级权限管道回归：查询请求与无标记命令透放、匿名 / 登录即可命令放行、超管短路不触达权限校验器、
/// 未认证拒绝（40100）、无权限拒绝（40300）、有权限放行。
/// </summary>
[TestFixture]
public class PermissionBehaviorTests
{
    /// <summary>查询请求（不实现 ICommand），权限管道必须直接透放。</summary>
    private sealed record TestQuery : IRequest<string>;

    /// <summary>未标注任何权限策略的写命令（运行期默认放行）。</summary>
    private sealed record UnmarkedCommand : IRequest<string>, ICommand;

    /// <summary>匿名放行写命令。</summary>
    [AllowAnonymousCommand]
    private sealed record AnonymousCommand : IRequest<string>, ICommand;

    /// <summary>登录即可写命令。</summary>
    [AllowAnyAuthenticatedCommand]
    private sealed record AnyAuthenticatedCommand : IRequest<string>, ICommand;

    /// <summary>需要细粒度权限的写命令。</summary>
    [RequireCommandPermission(PermissionDefinitions.SystemUserAdd)]
    private sealed record RestrictedCommand : IRequest<string>, ICommand;

    /// <summary>构造权限行为。</summary>
    /// <typeparam name="TRequest">请求类型。</typeparam>
    /// <param name="permissionChecker">权限校验器桩。</param>
    /// <param name="user">当前用户上下文桩。</param>
    /// <returns>权限行为实例。</returns>
    private static PermissionBehavior<TRequest, string> CreateBehavior<TRequest>(
        IPermissionChecker permissionChecker, IUserContext user)
        where TRequest : notnull
        => new(permissionChecker, user, NullLogger<PermissionBehavior<TRequest, string>>.Instance);

    /// <summary>查询请求不实现 ICommand，权限管道直接透放且不触达权限校验器。</summary>
    [Test]
    public async Task QueryRequest_PassesThrough_WithoutPermissionCheck()
    {
        var behavior = CreateBehavior<TestQuery>(new ThrowingPermissionChecker(), new StubUserContext(authenticated: false));
        var calls = 0;
        Task<string> Next() { calls++; return Task.FromResult("ok"); }

        var result = await behavior.Handle(new TestQuery(), _ => Next(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("ok"));
            Assert.That(calls, Is.EqualTo(1), "查询请求应直接透放到处理器");
        });
    }

    /// <summary>未标注权限策略的写命令运行期默认放行且不触达权限校验器。</summary>
    [Test]
    public async Task UnmarkedCommand_PassesThrough_WithoutPermissionCheck()
    {
        var behavior = CreateBehavior<UnmarkedCommand>(new ThrowingPermissionChecker(), new StubUserContext(authenticated: false));
        var calls = 0;
        Task<string> Next() { calls++; return Task.FromResult("ok"); }

        var result = await behavior.Handle(new UnmarkedCommand(), _ => Next(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("ok"));
            Assert.That(calls, Is.EqualTo(1));
        });
    }

    /// <summary>匿名命令在未认证时也直接放行，不触达权限校验器。</summary>
    [Test]
    public async Task AnonymousCommand_PassesThrough_WhenUnauthenticated()
    {
        var behavior = CreateBehavior<AnonymousCommand>(new ThrowingPermissionChecker(), new StubUserContext(authenticated: false));
        var calls = 0;
        Task<string> Next() { calls++; return Task.FromResult("ok"); }

        var result = await behavior.Handle(new AnonymousCommand(), _ => Next(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("ok"));
            Assert.That(calls, Is.EqualTo(1));
        });
    }

    /// <summary>登录即可命令对已认证用户放行，不触达细粒度权限校验。</summary>
    [Test]
    public async Task AnyAuthenticatedCommand_PassesThrough_WhenAuthenticated()
    {
        var behavior = CreateBehavior<AnyAuthenticatedCommand>(new ThrowingPermissionChecker(), new StubUserContext(authenticated: true));
        var calls = 0;
        Task<string> Next() { calls++; return Task.FromResult("ok"); }

        var result = await behavior.Handle(new AnyAuthenticatedCommand(), _ => Next(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("ok"));
            Assert.That(calls, Is.EqualTo(1));
        });
    }

    /// <summary>超管在细粒度权限校验前短路放行，权限校验器任何调用即抛异常以断言不触达。</summary>
    [Test]
    public async Task SuperAdmin_ShortCircuits_PermissionCheckerNotTouched()
    {
        var behavior = CreateBehavior<RestrictedCommand>(
            new ThrowingPermissionChecker(), new StubUserContext(authenticated: true, superAdmin: true));
        var calls = 0;
        Task<string> Next() { calls++; return Task.FromResult("ok"); }

        var result = await behavior.Handle(new RestrictedCommand(), _ => Next(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("ok"));
            Assert.That(calls, Is.EqualTo(1), "超管应直接放行，不得触发 HasPermissionAsync");
        });
    }

    /// <summary>未认证用户执行受限命令抛 40100，且不得执行处理器。</summary>
    [Test]
    public void Unauthenticated_Throws40100()
    {
        var behavior = CreateBehavior<RestrictedCommand>(new ThrowingPermissionChecker(), new StubUserContext(authenticated: false));
        var calls = 0;
        Task<string> Next() { calls++; return Task.FromResult("ok"); }

        var exception = Assert.ThrowsAsync<BusinessException>(async () =>
            await behavior.Handle(new RestrictedCommand(), _ => Next(), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Code, Is.EqualTo(ErrorCodes.AuthUnauthorized));
            Assert.That(calls, Is.Zero, "拒绝时不得执行处理器");
        });
    }

    /// <summary>已认证但无权限的用户执行受限命令抛 40300，且不得执行处理器。</summary>
    [Test]
    public void NoPermission_Throws40300()
    {
        var behavior = CreateBehavior<RestrictedCommand>(new StubPermissionChecker(granted: false), new StubUserContext(authenticated: true));
        var calls = 0;
        Task<string> Next() { calls++; return Task.FromResult("ok"); }

        var exception = Assert.ThrowsAsync<BusinessException>(async () =>
            await behavior.Handle(new RestrictedCommand(), _ => Next(), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Code, Is.EqualTo(ErrorCodes.AuthForbidden));
            Assert.That(calls, Is.Zero, "拒绝时不得执行处理器");
        });
    }

    /// <summary>已认证且拥有权限的用户执行受限命令正常透放。</summary>
    [Test]
    public async Task GrantedPermission_PassesThrough()
    {
        var behavior = CreateBehavior<RestrictedCommand>(new StubPermissionChecker(granted: true), new StubUserContext(authenticated: true));
        var calls = 0;
        Task<string> Next() { calls++; return Task.FromResult("ok"); }

        var result = await behavior.Handle(new RestrictedCommand(), _ => Next(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("ok"));
            Assert.That(calls, Is.EqualTo(1));
        });
    }

    /// <summary>权限校验器桩：按构造参数固定返回是否拥有权限。</summary>
    /// <param name="granted">HasPermissionAsync 的返回值。</param>
    private sealed class StubPermissionChecker(bool granted) : IPermissionChecker
    {
        /// <inheritdoc />
        public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("本测试不应调用 GetPermissionsAsync");

        /// <inheritdoc />
        public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
            => Task.FromResult(granted);

        /// <inheritdoc />
        public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public Task InvalidateAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>权限校验器桩：任何细粒度校验调用都抛异常（放行 / 短路路径不应触达）。</summary>
    private sealed class ThrowingPermissionChecker : IPermissionChecker
    {
        /// <inheritdoc />
        public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("放行 / 短路路径不应触发细粒度权限校验");

        /// <inheritdoc />
        public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("放行 / 短路路径不应触发细粒度权限校验");

        /// <inheritdoc />
        public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public Task InvalidateAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>当前用户上下文桩：认证态与超管态由构造参数控制。</summary>
    /// <param name="authenticated">是否已认证。</param>
    /// <param name="superAdmin">是否超级管理员。</param>
    private sealed class StubUserContext(bool authenticated, bool superAdmin = false) : IUserContext
    {
        /// <inheritdoc />
        public Guid? UserId { get; } = authenticated ? Guid.NewGuid() : null;

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
        public bool IsSuperAdmin => superAdmin;
    }
}
