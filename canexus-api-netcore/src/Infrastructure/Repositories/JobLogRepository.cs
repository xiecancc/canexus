
using System.Linq.Expressions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Paging;
using Infrastructure.Shared.Tenants;
using SqlSugar;

namespace Infrastructure.Repositories;
/// <summary>基于 SqlSugar 的定时任务执行日志仓储实现（按触发时间倒序分页，最新记录在前）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="currentUser">当前用户上下文。</param>
/// <param name="tenantContext">当前租户上下文（写入侧显式补偿租户条件）。</param>
public class JobLogRepository(ISqlSugarClient client, IUserContext currentUser, ITenantContext tenantContext) : AggregateRepository<SysJobLog>(client, currentUser, tenantContext), IJobLogRepository
{
    /// <inheritdoc />
    public async Task<PagedResponse<SysJobLog>> GetPagedDescAsync(
        PagedRequest request,
        Expression<Func<SysJobLog, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var total = new RefAsync<int>();
        var query = Client.Queryable<SysJobLog>();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        // 主键为随机 Guid，按 Id 排序不代表时间顺序；执行日志按触发时间倒序、同秒并列按 Id 保证稳定
        // （SqlSugar 连续 OrderBy 调用按顺序拼接为多级排序）
        var items = await query.OrderBy(it => it.TriggerTime, OrderByType.Desc)
            .OrderBy(it => it.Id, OrderByType.Desc)
            .ToPageListAsync(request.PageIndex, request.PageSize, total);
        return PagedResponse<SysJobLog>.Create(items, total.Value, request);
    }
}
