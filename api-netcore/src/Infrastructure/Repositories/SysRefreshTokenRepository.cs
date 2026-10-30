namespace Infrastructure.Repositories;

using Domain.Entities;
using Domain.Repositories;
using SqlSugar;

/// <summary>基于 SqlSugar 的刷新令牌专属仓储实现（继承聚合仓储，补充令牌语义方法）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
public class SysRefreshTokenRepository(ISqlSugarClient client) : AggregateRepository<SysRefreshToken>(client), ISysRefreshTokenRepository
{
    /// <inheritdoc />
    public virtual async Task<SysRefreshToken?> GetActiveByTokenAsync(string token, DateTime now, CancellationToken cancellationToken = default)
    {
        return await Client.Queryable<SysRefreshToken>()
            .FirstAsync(t => t.Token == token && t.RevokedAt == null && t.ExpiresAt > now);
    }

    /// <inheritdoc />
    public virtual async Task<int> RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken cancellationToken = default)
    {
        return await Client.Updateable<SysRefreshToken>()
            .SetColumns(t => t.RevokedAt == revokedAt)
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > revokedAt)
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<int> RemoveExpiredAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        return await Client.Deleteable<SysRefreshToken>()
            .Where(t => t.ExpiresAt < now || t.RevokedAt != null)
            .ExecuteCommandAsync(cancellationToken);
    }
}
