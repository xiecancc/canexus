
using Domain.Entities;
using Domain.Shared.Paging;

namespace Domain.Repositories;
/// <summary>登录日志仓储：在聚合仓储能力之上提供日志专属的物理清空能力与按时间倒序分页（日志表不走软删除墓碑）。</summary>
public interface ILoginLogRepository : IAggregateRepository<SysLoginLog>
{
    /// <summary>清空全部登录日志（物理删除）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>按 Id 物理删除单条登录日志（与清空策略一致）。</summary>
    /// <param name="id">日志 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>按登录时间倒序分页查询（最新记录在前，LoginTime 相同时按 Id 倒序稳定排序）。</summary>
    /// <param name="request">分页请求。</param>
    /// <param name="predicate">过滤条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public Task<PagedResponse<SysLoginLog>> GetPagedByTimeDescAsync(
        PagedRequest request,
        System.Linq.Expressions.Expression<Func<SysLoginLog, bool>>? predicate = null,
        CancellationToken cancellationToken = default);
}
