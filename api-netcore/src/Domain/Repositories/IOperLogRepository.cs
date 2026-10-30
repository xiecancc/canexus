namespace Domain.Repositories;

using Domain.Entities;

/// <summary>操作日志仓储：在聚合仓储能力之上提供日志专属的物理清空能力（日志表不走软删除墓碑）。</summary>
public interface IOperLogRepository : IAggregateRepository<SysOperLog>
{
    /// <summary>清空全部操作日志（物理删除）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>按 Id 物理删除单条操作日志（与清空策略一致）。</summary>
    /// <param name="id">日志 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
