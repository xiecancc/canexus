
using API.Authorization;
using Application.Permissions;
using Domain.Services.Users;
using Domain.Shared.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace API.Filters;
/// <summary>
/// 全局权限过滤器：解析端点上的 <see cref="RequirePermissionAttribute"/>，校验当前用户权限。
/// <list type="bullet">
/// <item>匿名端点（AllowAnonymous）→ 放行；</item>
/// <item>带 [RequirePermission] → 未认证 401 统一响应（code 40100）；超管（roleKey=super_admin）放行；无权限 403（code 40300）；</item>
/// <item>带 [AllowAnyAuthenticated] → 未认证 401；已认证放行；</item>
/// <item>权限声明与豁免皆无 → 默认拒绝 403（P0 权限默认拒绝运行期兜底；正常应在启动期反向扫描即被拦截）。</item>
/// </list>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="permissionChecker">权限校验器。</param>
/// <param name="currentUser">当前用户上下文。</param>
/// <param name="logger">日志器（记录权限拒绝事件）。</param>
public sealed class PermissionAuthorizationFilter(
    IPermissionChecker permissionChecker,
    IUserContext currentUser,
    ILogger<PermissionAuthorizationFilter> logger) : IAsyncAuthorizationFilter
{
    /// <inheritdoc />
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // 匿名端点直接放行（AllowAnonymous 由端点元数据声明）
        if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return;
        }

        var permission = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<RequirePermissionAttribute>();
        if (permission is null)
        {
            // 登录即可豁免端点：要求已认证，不做细粒度权限校验
            if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<AllowAnyAuthenticatedAttribute>() is not null)
            {
                if (!currentUser.IsAuthenticated)
                {
                    context.Result = Unauthorized();
                }

                return;
            }

            // 默认拒绝（P0）：受保护端点必须声明权限或显式豁免；漏写声明的端点在此被拒绝，
            // 与启动期反向扫描（PermissionsStartupValidator.FindUnauthorizedEndpoints）构成双保险
            logger.LogError("权限默认拒绝：端点 {Path} 未声明 [RequirePermission] 也未声明豁免，已拒绝访问（应在启动期反向扫描中修复）",
                context.HttpContext.Request.Path);
            context.Result = new JsonResult(Result.Fail(Domain.Shared.Results.ErrorCodes.AuthForbidden,
                "端点未声明访问策略，已默认拒绝"))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        if (!currentUser.IsAuthenticated)
        {
            logger.LogWarning("权限校验拒绝：未认证请求访问 {Permission}（路径 {Path}）",
                permission.Permission, context.HttpContext.Request.Path);
            context.Result = new JsonResult(Result.Fail(Domain.Shared.Results.ErrorCodes.AuthUnauthorized, "未登录或令牌无效"))
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        if (currentUser.IsSuperAdmin)
        {
            return;
        }

        var userId = currentUser.UserId!.Value;
        if (await permissionChecker.HasPermissionAsync(userId, permission.Permission))
        {
            return;
        }

        context.Result = new JsonResult(Result.Fail(Domain.Shared.Results.ErrorCodes.AuthForbidden, "无权限访问"))
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
        logger.LogWarning("权限校验拒绝：用户 {UserId} 无 {Permission} 权限（路径 {Path}）",
            userId, permission.Permission, context.HttpContext.Request.Path);
    }

    /// <summary>401 统一响应。</summary>
    private static JsonResult Unauthorized() => new(Result.Fail(Domain.Shared.Results.ErrorCodes.AuthUnauthorized, "未登录或令牌无效"))
    {
        StatusCode = StatusCodes.Status401Unauthorized
    };
}
