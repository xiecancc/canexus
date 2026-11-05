using API.Authorization;
using Application.Events;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Results;
using Infrastructure.Shared.Caching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace API.Middlewares;
/// <summary>
/// 强制改密中间件（P0-S4 初始密码策略 / P1-R6 缓存化）：认证之后拦截"待改密"用户对受保护端点的访问。
/// <list type="bullet">
/// <item>未认证、匿名端点（AllowAnonymous）、公开端点（无 [Authorize] / RequirePermission）→ 放行；</item>
/// <item>带 <see cref="AllowWhileMustChangePasswordAttribute"/> 的白名单端点（自助改密 / 用户信息 / 路由 / 登出）→ 放行；</item>
/// <item>用户 MustChangePassword=true 访问其余受保护端点 → 403（40302 须先修改初始密码）。</item>
/// </list>
/// <para>标记读取经双层缓存（Hot 档：L1 30s / L2 5min）——该标记变更频率极低，逐请求查库是纯开销；
/// 缓存失效由 <see cref="MustChangePasswordCacheInvalidationHandler"/> 经密码重置领域事件驱动
///（自助改密 / 管理员重置密码两个变更点），未命中失效窗口内最迟约 5 分钟收敛到最新值。</para>
/// </summary>
/// <remarks>构造函数（由请求管道注入）。</remarks>
/// <param name="next">管道中的下一个委托。</param>
/// <param name="logger">日志器（记录强制改密拦截事件）。</param>
public sealed class MustChangePasswordMiddleware(
    RequestDelegate next,
    ILogger<MustChangePasswordMiddleware> logger)
{
    /// <summary>中间件入口：按认证状态、端点豁免声明与用户强制改密标记决定放行或拦截。</summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="currentUser">当前用户上下文。</param>
    /// <param name="userRepository">用户聚合仓储（缓存未命中时回源读取强制改密标记）。</param>
    /// <param name="cache">应用缓存端口（双层缓存，承载强制改密标记）。</param>
    public async Task InvokeAsync(
        HttpContext context,
        IUserContext currentUser,
        IAggregateRepository<SysUser> userRepository,
        ICache cache)
    {
        if (!currentUser.IsAuthenticated)
        {
            await next(context);
            return;
        }

        var endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            await next(context);
            return;
        }

        // 匿名端点与公开端点（无授权要求）一律放行
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(context);
            return;
        }

        var requiresAuth = endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null
            || endpoint.Metadata.GetMetadata<RequirePermissionAttribute>() is not null;
        if (!requiresAuth)
        {
            await next(context);
            return;
        }

        // 强制改密白名单端点放行
        if (endpoint.Metadata.GetMetadata<AllowWhileMustChangePasswordAttribute>() is not null)
        {
            await next(context);
            return;
        }

        var userId = currentUser.UserId;
        if (userId is null)
        {
            await next(context);
            return;
        }

        // 强制改密标记经双层缓存读取（Hot 档），未命中回源查库；失效由密码重置领域事件驱动
        var mustChangePassword = await cache.GetOrCreateAsync(
            MustChangePasswordCacheInvalidationHandler.CacheKey(userId.Value),
            async token =>
            {
                var user = await userRepository.GetAsync([it => it.Id == userId.Value], token);
                return user?.MustChangePassword == true;
            },
            CacheTtl.Hot,
            context.RequestAborted);

        if (!mustChangePassword)
        {
            await next(context);
            return;
        }

        logger.LogWarning("强制改密拦截：用户 {UserId} 尚未修改初始密码（路径 {Path}）", userId, context.Request.Path);
        await UnifiedResponseWriter.WriteResultAsync(
            context, StatusCodes.Status403Forbidden,
            Result.Fail(ErrorCodes.MustChangePasswordRequired, "首次登录须先修改初始密码"));
    }
}
