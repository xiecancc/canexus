
using Domain.Entities;

namespace Domain.Repositories;
/// <summary>操作日志仓储：在聚合仓储能力之上提供日志专属的物理清空能力（日志表不走软删除墓碑）。</summary>
public interface IOperLogRepository : IAggregateRepository<SysOperLog>
{
    /// <summary>按时间范围清空操作日志（物理删除；仅删除 CreatedAt 落在 [fromInclusive, toExclusive) 且当前租户可见的记录）。</summary>
    /// <param name="fromInclusive">起始时间（含，UTC）。</param>
    /// <param name="toExclusive">结束时间（不含，UTC）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task ClearAsync(DateTime fromInclusive, DateTime toExclusive, CancellationToken cancellationToken = default);

    /// <summary>按 Id 物理删除单条操作日志（与清空策略一致）。</summary>
    /// <param name="id">日志 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
