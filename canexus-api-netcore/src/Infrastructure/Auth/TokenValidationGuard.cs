
using System.Security.Claims;
using Domain.Services.Auth;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Auth;
/// <summary>令牌撤销校验端口：在 JWT 验签通过后、请求进入业务管道前，校验用户级令牌版本与会话吊销状态。</summary>
public interface ITokenValidationGuard
{
    /// <summary>校验令牌是否仍然有效（令牌版本未变、用户仍可用、会话未被吊销）。</summary>
    /// <param name="principal">已验签的认证主体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>仍然有效返回 true；已失效返回 false（调用方应拒绝并返回 401）。</returns>
    public Task<bool> ValidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}

/// <summary>
/// 令牌撤销校验实现：读取 accessToken 的 tokenVersion / sid 声明，与缓存中的当前用户状态和会话吊销集合比对。
/// <para>兼容性：历史令牌缺少 tokenVersion 声明时按 0 处理（用户当前版本为 0 则继续有效，滚动发布不强制全量下线）；
/// 缺少 sid 声明时仅执行用户级校验。多租户 / 平台用户走同一校验，不区分身份类型。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="tokenVersionService">令牌版本服务（缓存承载请求期读取）。</param>
/// <param name="logger">日志器。</param>
public sealed class TokenValidationGuard(ITokenVersionService tokenVersionService, ILogger<TokenValidationGuard> logger) : ITokenValidationGuard
{
    /// <inheritdoc />
    public async Task<bool> ValidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var subject = principal.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            logger.LogWarning("令牌撤销校验失败：sub 声明缺失或格式非法");
            return false;
        }

        var state = await tokenVersionService.GetStateAsync(userId, cancellationToken);
        if (state is null || !state.Active)
        {
            logger.LogInformation("令牌撤销校验失败：用户 {UserId} 不存在、已删除或已停用", userId);
            return false;
        }

        var version = int.TryParse(principal.FindFirst("tokenVersion")?.Value, out var claimVersion)
            ? claimVersion
            : 0;
        if (version != state.Version)
        {
            logger.LogInformation("令牌撤销校验失败：用户 {UserId} 令牌版本已变更（令牌 {TokenVersion}，当前 {CurrentVersion}）",
                userId, version, state.Version);
            return false;
        }

        var sessionValue = principal.FindFirst("sid")?.Value;
        if (Guid.TryParse(sessionValue, out var sessionId)
            && sessionId != Guid.Empty
            && await tokenVersionService.IsSessionRevokedAsync(sessionId, cancellationToken))
        {
            logger.LogInformation("令牌撤销校验失败：用户 {UserId} 会话 {SessionId} 已被吊销", userId, sessionId);
            return false;
        }

        return true;
    }
}
