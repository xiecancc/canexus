
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Infrastructure.Shared.Tenants;
using SqlSugar;

namespace Infrastructure.Repositories;
/// <summary>基于 SqlSugar 的操作日志仓储实现（清空与单条删除均为物理删除）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="currentUser">当前用户上下文。</param>
/// <param name="tenantContext">当前租户上下文（写入侧显式补偿租户条件）。</param>
public class OperLogRepository(ISqlSugarClient client, IUserContext currentUser, ITenantContext tenantContext) : AggregateRepository<SysOperLog>(client, currentUser, tenantContext), IOperLogRepository
{
    /// <inheritdoc />
    public async Task ClearAsync(DateTime fromInclusive, DateTime toExclusive, CancellationToken cancellationToken = default)
    {
        // Deleteable 不走查询侧全局过滤器：显式补时间范围与租户条件，危险清空不得全表 / 跨租户执行
        var delete = Client.Deleteable<SysOperLog>()
            .Where(log => log.CreatedAt >= fromInclusive && log.CreatedAt < toExclusive);
        if (CurrentTenantId is { } tenantId)
        {
            delete = delete.Where(log => log.TenantId == tenantId);
        }

        await delete.ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var delete = Client.Deleteable<SysOperLog>().In(id);
        if (CurrentTenantId is { } tenantId)
        {
            delete = delete.Where(log => log.TenantId == tenantId);
        }

        await delete.ExecuteCommandAsync(cancellationToken);
    }
}
