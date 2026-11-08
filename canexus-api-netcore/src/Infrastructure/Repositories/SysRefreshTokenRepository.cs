
using System.Linq.Expressions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Paging;
using Infrastructure.Shared.Tenants;
using SqlSugar;

namespace Infrastructure.Repositories;
/// <summary>基于 SqlSugar 的刷新令牌专属仓储实现（继承聚合仓储，补充令牌语义方法）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="currentUser">当前用户上下文。</param>
/// <param name="tenantContext">当前租户上下文（写入侧显式补偿租户条件）。</param>
public class SysRefreshTokenRepository(ISqlSugarClient client, IUserContext currentUser, ITenantContext tenantContext) : AggregateRepository<SysRefreshToken>(client, currentUser, tenantContext), ISysRefreshTokenRepository
{
    /// <summary>刷新令牌为高频自写数据（轮换 / 吊销 / 过期清理），记录字段级变更日志无审计价值，关闭。</summary>
    protected override bool EntityChangeLogEnabled => false;

    /// <inheritdoc />
    public virtual async Task<SysRefreshToken?> GetActiveByTokenAsync(string token, DateTime now, CancellationToken cancellationToken = default)
    {
        return await BuildQuery()
            .Where(t => t.Token == token && t.RevokedAt == null && t.ExpiresAt > now)
            .FirstAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<SysRefreshToken?> GetRevokedByTokenAsync(string token, DateTime now, CancellationToken cancellationToken = default)
    {
        return await BuildQuery()
            .Where(t => t.Token == token && t.RevokedAt != null && t.ExpiresAt > now)
            .FirstAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<int> RevokeAllForSessionAsync(Guid sessionId, DateTime revokedAt, CancellationToken cancellationToken = default)
    {
        // 独立连接：重用检测发生在写命令事务内，随后业务异常会回滚该事务；
        // 安全吊销必须独立于业务事务提交，否则攻击者仍可继续使用该家族的新令牌。
        return await Client.CopyNew().Updateable<SysRefreshToken>()
            .SetColumns(t => t.RevokedAt == revokedAt)
            .Where(t => t.SessionId == sessionId && t.RevokedAt == null && t.ExpiresAt > revokedAt)
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<int> RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken cancellationToken = default)
    {
        // 刷新令牌吊销是匿名请求路径的可执行写入，不能依赖 QueryFilter：按令牌所属租户收敛
        var update = BuildUpdate()
            .SetColumns(t => t.RevokedAt == revokedAt)
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > revokedAt);
        if (CurrentTenantId is { } tenantId)
        {
            update = update.Where(t => t.TenantId == tenantId);
        }

        return await update.ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<int> RevokeByIdAsync(Guid id, DateTime revokedAt, CancellationToken cancellationToken = default)
    {
        var update = BuildUpdate()
            .SetColumns(t => t.RevokedAt == revokedAt)
            .Where(t => t.Id == id && t.RevokedAt == null && t.ExpiresAt > revokedAt);
        if (CurrentTenantId is { } tenantId)
        {
            update = update.Where(t => t.TenantId == tenantId);
        }

        return await update.ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<PagedResponse<SysRefreshToken>> GetPagedActiveAsync(
        PagedRequest request,
        DateTime now,
        Expression<Func<SysRefreshToken, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var total = new RefAsync<int>();
        var query = BuildQuery()
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
        var delete = BuildDelete(t => t.ExpiresAt < now || t.RevokedAt != null);
        if (CurrentTenantId is { } tenantId)
        {
            delete = delete.Where(t => t.TenantId == tenantId);
        }

        return await delete.ExecuteCommandAsync(cancellationToken);
    }
}
