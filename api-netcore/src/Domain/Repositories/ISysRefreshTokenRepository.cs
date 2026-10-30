namespace Domain.Repositories;

using Domain.Entities;

/// <summary>
/// 刷新令牌专属仓储接口：承载令牌轮换 / 吊销 / 过期清理的查询与批量语义。
/// <para>继承聚合仓储（CRUD），只补充意图命名方法；活跃令牌定义：未吊销（RevokedAt = null）且未过期（ExpiresAt &gt; 当前时间）。</para>
/// </summary>
public interface ISysRefreshTokenRepository : IAggregateRepository<SysRefreshToken>
{
    /// <summary>按令牌串获取活跃令牌（未吊销且未过期）；不存在或已失效返回 null。</summary>
    /// <param name="token">令牌串。</param>
    /// <param name="now">当前时间（由调用方传入，保证判断基准一致）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>活跃令牌实体；无效时返回 null。</returns>
    Task<SysRefreshToken?> GetActiveByTokenAsync(string token, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>吊销指定用户的全部活跃令牌（强制下线全部会话），单条 UPDATE 完成。</summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="revokedAt">吊销时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响的行数。</returns>
    Task<int> RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken cancellationToken = default);

    /// <summary>清理已过期或已吊销的令牌（墓碑回收），单条 DELETE 完成。</summary>
    /// <param name="now">当前时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除的行数。</returns>
    Task<int> RemoveExpiredAsync(DateTime now, CancellationToken cancellationToken = default);
}
