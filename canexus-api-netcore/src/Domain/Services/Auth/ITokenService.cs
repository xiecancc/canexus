
using System.Security.Claims;

namespace Domain.Services.Auth;
/// <summary>
/// 令牌发放与刷新令牌管理接口：
/// <list type="bullet">
/// <item>发放 accessToken（JWT，claims 含用户 / 部门 / 角色信息）与 refreshToken（随机串，持久化）；</item>
/// <item>校验 accessToken 并还原 <see cref="ClaimsPrincipal"/>（过期 / 签名无效返回 null）；</item>
/// <item>刷新令牌的活性查询与吊销（轮换由调用方组合：查活性 → 吊销旧 → 发新对）。</item>
/// </list>
/// </summary>
public interface ITokenService
{
    /// <summary>为指定用户发放令牌对（accessToken + refreshToken，refreshToken 同时持久化）。</summary>
    /// <param name="subject">令牌主题（用户与角色信息）。</param>
    /// <param name="ip">签发来源 IP（可选，持久化到 refreshToken 记录，供在线会话列表展示）。</param>
    /// <param name="userAgent">签发来源 User-Agent（可选，同上）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌对（ExpiresIn 为 accessToken 有效期秒数）。</returns>
    public Task<TokenPair> GenerateAsync(TokenSubject subject, string? ip = null, string? userAgent = null, CancellationToken cancellationToken = default);

    /// <summary>校验 accessToken：签名 / 签发者 / 受众 / 生命周期全部通过时返回认证主体，否则返回 null。</summary>
    /// <param name="accessToken">访问令牌。</param>
    /// <returns>认证主体；无效或已过期返回 null。</returns>
    public ClaimsPrincipal? ValidateAccessToken(string accessToken);

    /// <summary>查询活跃（未吊销且未过期）的刷新令牌信息。</summary>
    /// <param name="refreshToken">刷新令牌串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>刷新令牌信息；不存在、已吊销或已过期返回 null。</returns>
    public Task<RefreshTokenInfo?> GetActiveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>吊销刷新令牌（幂等：不存在或已吊销时静默忽略）。</summary>
    /// <param name="refreshToken">刷新令牌串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default);
}

/// <summary>令牌对：accessToken + refreshToken 与有效期。</summary>
/// <param name="AccessToken">访问令牌（JWT）。</param>
/// <param name="RefreshToken">刷新令牌（随机串）。</param>
/// <param name="ExpiresIn">访问令牌有效期（秒）。</param>
public sealed record TokenPair(string AccessToken, string RefreshToken, long ExpiresIn);

/// <summary>刷新令牌信息（避免在共享层暴露持久化实体）。</summary>
/// <param name="Id">记录主键。</param>
/// <param name="Token">刷新令牌串。</param>
/// <param name="UserId">所属用户 Id。</param>
/// <param name="ExpiresAt">过期时间。</param>
public sealed record RefreshTokenInfo(Guid Id, string Token, Guid UserId, DateTime ExpiresAt);

/// <summary>令牌主题：发放令牌所需的用户身份信息。</summary>
/// <param name="UserId">用户 Id（写入 sub claim）。</param>
/// <param name="UserName">用户名（写入 unique_name claim）。</param>
/// <param name="DeptId">部门 Id（写入 deptId claim，可空）。</param>
/// <param name="RoleIds">角色 Id 集合（每个写入一条 roleId claim）。</param>
/// <param name="RoleKeys">角色权限字符串集合（每个写入一条 role claim）。</param>
/// <param name="TenantId">租户 Id（写入 tenantId claim，可空）。</param>
public sealed record TokenSubject(
    Guid UserId,
    string UserName,
    Guid? DeptId,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> RoleKeys,
    Guid? TenantId);
