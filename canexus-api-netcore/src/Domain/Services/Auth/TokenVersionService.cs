
namespace Domain.Services.Auth;
/// <summary>
/// 令牌版本（安全戳）服务端口：维护"用户级令牌版本"与"会话级吊销集合"，供请求期校验与各类强制下线路径共用。
/// <para>用户级版本用于改密 / 停用 / 删除 / 强制全量下线 / 角色重分配等"整体失效"场景；
/// 会话级吊销用于单设备登出 / 管理员按会话强制下线，避免多设备登录时误伤其他设备。</para>
/// <para>实现必须以缓存承载请求期读取，避免每个请求回源数据库；缓存失效需跨实例广播（Redis 可用时）。</para>
/// </summary>
public interface ITokenVersionService
{
    /// <summary>读取用户当前令牌状态（版本 + 账号是否仍可用）；用户不存在时返回 null。</summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌状态；用户不存在返回 null。</returns>
    public Task<TokenVersionState?> GetStateAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>递增用户令牌版本（使该用户此前签发的所有 accessToken 立即失效），并清除缓存。</summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>批量递增用户令牌版本（角色级变更影响该角色全部成员时使用），并清除缓存。</summary>
    /// <param name="userIds">用户 Id 集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task RevokeAllForUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>吊销单个会话（写入吊销集合，有效期覆盖 accessToken 剩余生命周期）。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>判断会话是否已被吊销。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已吊销返回 true。</returns>
    public Task<bool> IsSessionRevokedAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

/// <summary>用户令牌状态（请求期校验所需的最小信息）。</summary>
/// <param name="Version">当前令牌版本（与 accessToken 的 tokenVersion 声明比对）。</param>
/// <param name="Active">账号是否仍可用（存在、启用、未删除）。</param>
public sealed record TokenVersionState(int Version, bool Active)
{
    /// <summary>用户不存在 / 已删除的哨兵值（版本为 -1，恒不可用）。</summary>
    public static TokenVersionState Missing { get; } = new(-1, false);
}

/// <summary>
/// 刷新令牌串格式约定：64 位十六进制 = 32 位会话标识（Guid "N" 格式）+ 32 位随机密钥。
/// <para>密钥部分提供 128 位随机熵；会话标识同时写入 accessToken 的 sid 声明，用于单会话吊销。
/// 数据库仍只存整串的 SHA-256 哈希，不存明文；会话标识本身不构成机密（与 JWT jti 同性质）。</para>
/// </summary>
public static class RefreshTokenFormat
{
    /// <summary>会话标识在刷新令牌串中的长度（32 位十六进制）。</summary>
    public const int SessionIdLength = 32;

    /// <summary>刷新令牌串总长度（保持 64 位十六进制，兼容既有长度约定）。</summary>
    public const int Length = 64;

    /// <summary>从刷新令牌明文串解析会话标识；格式不匹配时返回 null。</summary>
    /// <param name="refreshToken">刷新令牌明文串。</param>
    /// <returns>会话标识；无法解析返回 null。</returns>
    public static Guid? TryParseSessionId(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length != Length)
        {
            return null;
        }

        var prefix = refreshToken[..SessionIdLength];
        return Guid.TryParseExact(prefix, "N", out var sessionId) ? sessionId : null;
    }
}
