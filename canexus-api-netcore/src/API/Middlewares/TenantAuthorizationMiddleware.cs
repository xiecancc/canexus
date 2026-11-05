using Domain.Services.Users;
using Domain.Shared.Results;
using Infrastructure.Shared.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace API.Middlewares;
/// <summary>
/// 租户授权中间件（P0-S1 越权修复）：多租户开关（MultiTenancy:Enabled）启用时，在认证之后收敛请求租户：
/// <list type="bullet">
/// <item>登录用户（JWT 声明租户）：请求头租户与声明不一致 → 403（40301 跨租户拒绝）；无论是否带头，
/// 均复查声明租户当前可用性（不存在 / 停用 / 到期 → 403），随后以声明租户收敛；</item>
/// <item>平台用户（无租户声明）：请求头租户须存在、启用且未到期（400801 / 400805 / 400806），否则拒绝；未带头 → 平台数据范围；</item>
/// <item>匿名请求（登录 / 验证码 / 刷新）：请求头租户同样须校验可用后生效，用于登录时的租户数据范围收敛。</item>
/// </list>
/// 校验通过的租户写入 <see cref="ITenantContext"/>，由 SqlSugar 全局过滤器执行行级隔离。
/// </summary>
/// <remarks>构造函数（仅注入根作用域可解析的单例与管道委托；scoped 依赖经 InvokeAsync 参数按请求作用域注入）。</remarks>
/// <param name="next">管道中的下一个委托。</param>
/// <param name="tenantContext">租户上下文（AsyncLocal）。</param>
/// <param name="logger">日志器（记录租户拒绝事件）。</param>
public sealed class TenantAuthorizationMiddleware(
    RequestDelegate next,
    ITenantContext tenantContext,
    ILogger<TenantAuthorizationMiddleware> logger)
{
    /// <summary>中间件入口：按多租户开关与认证状态收敛并校验请求租户后继续管道。</summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="configuration">应用配置（读取多租户开关）。</param>
    /// <param name="tenantValidator">租户访问校验器（请求作用域解析）。</param>
    /// <param name="currentUser">当前用户上下文（认证后 claims）。</param>
    public async Task InvokeAsync(HttpContext context, IConfiguration configuration, ITenantAccessValidator tenantValidator, IUserContext currentUser)
    {
        if (!configuration.GetValue("MultiTenancy:Enabled", false))
        {
            await next(context);
            return;
        }

        var requested = tenantContext.TenantId;
        var userTenant = currentUser.IsAuthenticated ? currentUser.TenantId : null;

        // 登录用户：租户归属以 JWT 声明为准
        if (currentUser.IsAuthenticated && userTenant is not null)
        {
            if (requested is not null && requested != userTenant)
            {
                logger.LogWarning("租户越权拒绝：用户 {UserId} 请求租户 {Requested}，与声明租户 {UserTenant} 不一致（路径 {Path}）",
                    currentUser.UserId, requested, userTenant, context.Request.Path);
                await UnifiedResponseWriter.WriteResultAsync(
                    context, StatusCodes.Status403Forbidden,
                    Result.Fail(ErrorCodes.TenantForbidden, "跨租户访问被拒绝"));
                return;
            }

            // 全链路租户状态复查（S1）：已认证会话的 accessToken 可能在签发后因租户停用 / 到期 / 删除而失效，
            // 不能只依赖登录时与请求头比对；此处对声明租户统一复查（无论是否携带 X-Tenant-Id），
            // 校验结果经 ITenantAccessValidator 缓存（Medium 档），租户状态变更由领域事件监听器主动失效。
            var userTenantStatus = await tenantValidator.ValidateAsync(userTenant.Value, context.RequestAborted);
            if (userTenantStatus != TenantAccessStatus.Ok)
            {
                var (code, message) = userTenantStatus switch
                {
                    TenantAccessStatus.NotFound => (ErrorCodes.TenantNotFound, "租户不存在或已被删除，访问被拒绝"),
                    TenantAccessStatus.Disabled => (ErrorCodes.TenantDisabled, "租户已停用，访问被拒绝"),
                    _ => (ErrorCodes.TenantExpired, "租户已到期，访问被拒绝")
                };

                logger.LogWarning("租户拒绝：用户 {UserId} 的声明租户 {UserTenant} {Message}（路径 {Path}）",
                    currentUser.UserId, userTenant, message, context.Request.Path);
                await UnifiedResponseWriter.WriteResultAsync(
                    context, StatusCodes.Status403Forbidden,
                    Result.Fail(code, message));
                return;
            }

            tenantContext.TenantId = userTenant;
            await next(context);
            return;
        }

        // 平台用户（无租户声明）或匿名请求：请求头租户须校验可用
        if (requested is not null)
        {
            var status = await tenantValidator.ValidateAsync(requested.Value, context.RequestAborted);
            var (code, message) = status switch
            {
                TenantAccessStatus.NotFound => (ErrorCodes.TenantNotFound, "租户不存在"),
                TenantAccessStatus.Disabled => (ErrorCodes.TenantDisabled, "租户已停用"),
                TenantAccessStatus.Expired => (ErrorCodes.TenantExpired, "租户已到期"),
                _ => (0, string.Empty)
            };

            if (code != 0)
            {
                logger.LogWarning("租户拒绝：请求租户 {Requested} {Message}（路径 {Path}）", requested, message, context.Request.Path);
                await UnifiedResponseWriter.WriteResultAsync(
                    context, ErrorCodes.GetHttpStatusCode(code),
                    Result.Fail(code, message));
                return;
            }

            tenantContext.TenantId = requested;
        }

        await next(context);
    }
}
