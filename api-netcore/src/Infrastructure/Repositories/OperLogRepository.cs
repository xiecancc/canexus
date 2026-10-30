namespace Infrastructure.Repositories;

using Domain.Entities;
using Domain.Repositories;
using SqlSugar;

/// <summary>基于 SqlSugar 的操作日志仓储实现（清空与单条删除均为物理删除）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
public class OperLogRepository(ISqlSugarClient client) : AggregateRepository<SysOperLog>(client), IOperLogRepository
{
    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await Client.Deleteable<SysOperLog>().ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await Client.Deleteable<SysOperLog>().In(id).ExecuteCommandAsync(cancellationToken);
    }
}
