using System.Collections.Concurrent;
using System.Reflection;
using Application.Contracts.Abstractions;
using Application.Permissions;
using Domain.Services.Users;
using Domain.Shared.Exceptions;
using Domain.Shared.Permissions;
using Domain.Shared.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;
/// <summary>
/// 命令级权限管道行为（P0-5）：对全部写命令（<see cref="ICommand"/>）在处理器执行前按命令上声明的
/// 权限策略做准入校验，覆盖「端点漏标权限即无校验」的缺口。
/// <list type="bullet">
/// <item>[AllowAnonymousCommand]：直接放行；</item>
/// <item>[AllowAnyAuthenticatedCommand]：已认证即放行；</item>
/// <item>[RequireCommandPermission]：经 <see cref="IPermissionChecker"/> 校验权限标识；</item>
/// <item>未标注任何策略：运行期默认放行（漏标由启动期 CommandPermissionStartupValidator fail-closed 阻止启动）。</item>
/// </list>
/// <para>超管（<see cref="IUserContext.IsSuperAdmin"/>）在细粒度权限校验前短路放行，避免触发权限缓存 / 数据库加载。</para>
/// </summary>
/// <typeparam name="TRequest">请求类型。</typeparam>
/// <typeparam name="TResponse">响应类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="permissionChecker">权限校验器。</param>
/// <param name="currentUser">当前用户上下文。</param>
/// <param name="logger">日志器。</param>
public sealed class PermissionBehavior<TRequest, TResponse>(
    IPermissionChecker permissionChecker,
    IUserContext currentUser,
    ILogger<PermissionBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>命令类型 → 权限策略的反射结果缓存（null 表示未声明任何策略）。</summary>
    private static readonly ConcurrentDictionary<Type, CommandPermissionPolicy?> Policies = new();

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // 已取消的请求在管道入口即中止
        cancellationToken.ThrowIfCancellationRequested();
        // 仅拦截写命令；查询请求直接透放
        if (request is not ICommand)
        {
            return await next();
        }

        var policy = Policies.GetOrAdd(request.GetType(), static type => ResolvePolicy(type));
        // 未声明策略 / 匿名命令：运行期默认放行（启动期校验器负责 fail-closed）
        if (policy is null || policy.IsAnonymous)
        {
            return await next();
        }

        if (!currentUser.IsAuthenticated)
        {
            logger.LogWarning("命令权限校验失败：未认证用户尝试执行命令 {Command}", typeof(TRequest).Name);
            throw new BusinessException(ErrorCodes.AuthUnauthorized, "未登录或令牌无效");
        }

        // 登录即可命令与超管短路放行，不触达细粒度权限校验（避免权限缓存 / 数据库加载）
        if (policy.IsAnyAuthenticated || currentUser.IsSuperAdmin)
        {
            return await next();
        }

        var userId = currentUser.UserId
            ?? throw new BusinessException(ErrorCodes.AuthUnauthorized, "未登录或令牌无效");

        if (await permissionChecker.HasPermissionAsync(userId, policy.Permission!, cancellationToken))
        {
            return await next();
        }

        logger.LogWarning("命令权限校验失败：用户无权执行命令 {Command}（需要权限 {Permission}）",
            typeof(TRequest).Name, policy.Permission);
        throw new BusinessException(ErrorCodes.AuthForbidden, "无权限访问");
    }

    /// <summary>按优先级解析命令声明的权限策略：匿名 → 登录即可 → 需权限；均未声明返回 null。</summary>
    /// <param name="commandType">写命令的运行时类型。</param>
    /// <returns>解析得到的权限策略；未声明任何策略时返回 null。</returns>
    private static CommandPermissionPolicy? ResolvePolicy(Type commandType)
    {
        if (commandType.IsDefined(typeof(AllowAnonymousCommandAttribute), inherit: false))
        {
            return new CommandPermissionPolicy(null, IsAnonymous: true, IsAnyAuthenticated: false);
        }

        if (commandType.IsDefined(typeof(AllowAnyAuthenticatedCommandAttribute), inherit: false))
        {
            return new CommandPermissionPolicy(null, IsAnonymous: false, IsAnyAuthenticated: true);
        }

        var require = commandType.GetCustomAttribute<RequireCommandPermissionAttribute>(inherit: false);
        return require is null
            ? null
            : new CommandPermissionPolicy(require.Permission, IsAnonymous: false, IsAnyAuthenticated: false);
    }

    /// <summary>命令权限策略（反射解析结果的缓存值）。</summary>
    /// <param name="Permission">所需权限标识（匿名 / 登录即可策略为 null）。</param>
    /// <param name="IsAnonymous">是否匿名放行。</param>
    /// <param name="IsAnyAuthenticated">是否登录即可放行。</param>
    private sealed record CommandPermissionPolicy(string? Permission, bool IsAnonymous, bool IsAnyAuthenticated);
}
