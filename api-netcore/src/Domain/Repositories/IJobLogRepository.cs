
using System.Linq.Expressions;
using Domain.Entities;
using Domain.Shared.Paging;

namespace Domain.Repositories;
/// <summary>定时任务执行日志仓储：在聚合仓储能力之上提供按触发时间倒序的分页查询（最新记录在前）。</summary>
public interface IJobLogRepository : IAggregateRepository<SysJobLog>
{
    /// <summary>按触发时间倒序分页查询执行日志（最新记录在前；主键为随机 Guid，不做时间代理）。</summary>
    /// <param name="request">分页请求。</param>
    /// <param name="predicate">过滤条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public Task<PagedResponse<SysJobLog>> GetPagedDescAsync(
        PagedRequest request,
        Expression<Func<SysJobLog, bool>>? predicate = null,
        CancellationToken cancellationToken = default);
}
