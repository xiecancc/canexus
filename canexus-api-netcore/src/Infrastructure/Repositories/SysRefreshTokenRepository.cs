
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Paging;
using SqlSugar;
using System.Linq.Expressions;

namespace Infrastructure.Repositories;
/// <summary>基于 SqlSugar 的刷新令牌专属仓储实现（继承聚合仓储，补充令牌语义方法）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="currentUser">当前用户上下文。</param>
public class SysRefreshTokenRepository(ISqlSugarClient client, IUserContext currentUser) : AggregateRepository<SysRefreshToken>(client, currentUser), ISysRefreshTokenRepository
{
    /// <summary>刷新令牌为高频自写数据（轮换 / 吊销 / 过期清理），记录字段级变更日志无审计价值，关闭。</summary>
    protected override bool EntityChangeLogEnabled => false;

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
    public virtual async Task<int> RevokeByIdAsync(Guid id, DateTime revokedAt, CancellationToken cancellationToken = default)
    {
        return await Client.Updateable<SysRefreshToken>()
            .SetColumns(t => t.RevokedAt == revokedAt)
            .Where(t => t.Id == id && t.RevokedAt == null && t.ExpiresAt > revokedAt)
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<PagedResponse<SysRefreshToken>> GetPagedActiveAsync(
        PagedRequest request,
        DateTime now,
        Expression<Func<SysRefreshToken, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var total = new RefAsync<int>();
        var query = Client.Queryable<SysRefreshToken>()
            .Where(t => t.RevokedAt == null && t.ExpiresAt > now);
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        var items = await query
            .OrderBy(t => t.CreatedAt, OrderByType.Desc)
            .OrderBy(t => t.Id, OrderByType.Desc)
            .ToPageListAsync(request.PageIndex, request.PageSize, total);
        return PagedResponse<SysRefreshToken>.Create(items, total.Value, request);
    }

    /// <inheritdoc />
    public virtual async Task<int> RemoveExpiredAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        return await Client.Deleteable<SysRefreshToken>()
            .Where(t => t.ExpiresAt < now || t.RevokedAt != null)
            .ExecuteCommandAsync(cancellationToken);
    }
}
