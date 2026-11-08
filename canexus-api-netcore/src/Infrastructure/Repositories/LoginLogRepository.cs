
using System.Linq.Expressions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Paging;
using Infrastructure.Shared.Tenants;
using SqlSugar;

namespace Infrastructure.Repositories;
/// <summary>基于 SqlSugar 的登录日志仓储实现（清空与单条删除均为物理删除，支持按登录时间倒序分页）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="currentUser">当前用户上下文。</param>
/// <param name="tenantContext">当前租户上下文（写入侧显式补偿租户条件）。</param>
public class LoginLogRepository(ISqlSugarClient client, IUserContext currentUser, ITenantContext tenantContext) : AggregateRepository<SysLoginLog>(client, currentUser, tenantContext), ILoginLogRepository
{
    /// <inheritdoc />
    public async Task InsertIndependentAsync(SysLoginLog log, CancellationToken cancellationToken = default)
    {
        // CopyNew：独立连接/事务作用域，环境事务回滚不影响本写入（审计存活语义见接口文档）。
        // 独立连接可能不复用当前请求的 Aop 租户填充，故显式补齐归属（已赋值的租户不被覆盖）。
        log.TenantId ??= CurrentTenantId;
        await Client.CopyNew().Insertable(log).ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ClearAsync(DateTime fromInclusive, DateTime toExclusive, CancellationToken cancellationToken = default)
    {
        var delete = BuildHardDelete(log => log.CreatedAt >= fromInclusive && log.CreatedAt < toExclusive);
        if (CurrentTenantId is { } tenantId)
        {
            delete = delete.Where(log => log.TenantId == tenantId);
        }

        await delete.ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var delete = BuildHardDelete(log => log.Id == id);
        if (CurrentTenantId is { } tenantId)
        {
            delete = delete.Where(log => log.TenantId == tenantId);
        }

        await delete.ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagedResponse<SysLoginLog>> GetPagedByTimeDescAsync(
        PagedRequest request,
        Expression<Func<SysLoginLog, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var total = new RefAsync<int>();
        var query = BuildQuery();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        var items = await query
            .OrderBy(it => it.LoginTime, OrderByType.Desc)
            .OrderBy(it => it.Id, OrderByType.Desc)
            .ToPageListAsync(request.PageIndex, request.PageSize, total);
        return PagedResponse<SysLoginLog>.Create(items, total.Value, request);
    }
}
