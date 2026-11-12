
using API.Middlewares;
using Domain.Shared.Localization;
using Domain.Shared.Results;
using Infrastructure.Auth;
using Infrastructure.Shared.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace API.Extensions;
/// <summary>认证授权的依赖注册扩展。</summary>
public static class AuthDependencyExtensions
{
    /// <summary>令牌撤销校验失败标记（写入 HttpContext.Items，由 OnChallenge 区分错误码）。</summary>
    private const string TokenRevokedReasonKey = "Auth:TokenRevoked";

    /// <summary>
    /// 注册 JWT Bearer 认证与授权：
    /// <list type="bullet">
    /// <item>JwtOption 由启动时的 RegisterOptions 统一注册，验签参数经 IOptionsMonitor 从注册结果获取；</item>
    /// <item>claims 不做入站映射（sub / unique_name / role 等保持原名，与 Infrastructure.Users.HttpContextUserContext 对齐）；</item>
    /// <item>认证失败（401 挑战）返回统一响应结构（令牌过期返回 AuthTokenExpired，否则 AuthUnauthorized）。</item>
    /// </list>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        // JwtOption 的注册由启动时的 RegisterOptions 统一完成；
        // IOptionsMonitor<JwtOption> 供令牌服务 / 验签参数读取运行时最新配置（Secret 支持热加载）

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.Events = new JwtBearerEvents
                {
                    // 令牌撤销校验（P1-A）：验签通过后再比对用户令牌版本 / 会话吊销状态；
                    // 失败即标记原因并拒绝认证，由 OnChallenge 返回明确的 401 错误码
                    OnTokenValidated = async context =>
                    {
                        var guard = context.HttpContext.RequestServices.GetRequiredService<ITokenValidationGuard>();
                        if (!await guard.ValidateAsync(context.Principal!, context.HttpContext.RequestAborted))
                        {
                            context.HttpContext.Items[TokenRevokedReasonKey] = true;
                            context.Fail("令牌已撤销");
                        }
                    },

                    // 401 挑战：返回与全局一致的 { code, message, data } 结构
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        var revoked = context.HttpContext.Items.ContainsKey(TokenRevokedReasonKey);
                        var expired = context.AuthenticateFailure is SecurityTokenExpiredException;
                        var (code, message) = revoked
                            ? (ErrorCodes.AuthTokenRevoked, "登录状态已失效，请重新登录")
                            : expired
                                ? (ErrorCodes.AuthTokenExpired, "令牌已过期，请重新登录")
                                : (ErrorCodes.AuthUnauthorized, "未登录或令牌无效");
                        var result = Result.Fail(code, ErrorMessageLocalizer.Localize(code, message));
                        await UnifiedResponseWriter.WriteResultAsync(
                            context.HttpContext, StatusCodes.Status401Unauthorized, result);
                    }
                };
            });

        // 动态 TokenValidationParameters：JwtBearerOptions 的 Configure 委托在首次解析时执行，
        // 校验参数取自监视器当前绑定结果，签发密钥经 IssuerSigningKeyResolver 在每次验签时读取最新配置，
        // 与令牌服务（签发侧）一起实现 Jwt:Secret 热加载；Issuer / Audience 等静态字段启动定型
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptionsMonitor<JwtOption>>((bearer, monitor) =>
                bearer.TokenValidationParameters = JwtTokenService.BuildValidationParameters(
                    monitor.CurrentValue,
                    () => monitor.CurrentValue.Secret));

        // 默认拒绝（P0）：未显式标注 [AllowAnonymous] 的端点（含新增 minimal API）一律要求已认证，
        // 与 MVC 全局 PermissionAuthorizationFilter 构成双保险；健康检查端点在 Program.cs 显式放行
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());
        return services;
    }
}
