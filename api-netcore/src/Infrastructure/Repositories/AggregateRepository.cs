namespace Infrastructure.Repositories;

using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using SqlSugar;

/// <summary>
/// 基于 SqlSugar 的聚合仓储实现（对应 <see cref="AggregateBase"/>）：
/// 继承领域仓储（查询 / 插入 / 物理删除），重写删除为软删除，并补充乐观并发更新与恢复能力。
/// <para>审计字段（创建/更新人与时间、软删除标记）与空 Guid 主键由 Aop.DataExecuting 自动填充；
/// 更新由仓储显式做乐观并发控制（UPDATE ... WHERE Version = 期望值，受影响 0 行即冲突），
/// 版本冲突时抛出业务异常（错误码 CommonConcurrencyConflict）。</para>
/// </summary>
/// <typeparam name="TEntity">聚合根实体类型。</typeparam>
public class AggregateRepository<TEntity> : DomainRepository<TEntity>, IAggregateRepository<TEntity>
    where TEntity : AggregateBase, new()
{
    /// <summary>构造函数。</summary>
    /// <param name="client">SqlSugar 客户端。</param>
    public AggregateRepository(ISqlSugarClient client)
        : base(client)
    {
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        // 显式乐观锁：UPDATE ... WHERE Id = @id AND Version = 期望值；
        // 受影响 0 行说明库中版本已被他人推进，抛并发冲突业务异常。
        // 更新人/更新时间仍由 Aop.DataExecuting 自动填充；版本号在本方法内统一递增。
        var expected = entity.Version;
        entity.Version = expected + 1;
        var rows = await Client.Updateable(entity)
            .Where(it => it.Id == entity.Id && it.Version == expected)
            .ExecuteCommandAsync(cancellationToken);

        if (rows == 0)
        {
            throw new BusinessException(
                ErrorCodes.CommonConcurrencyConflict,
                "数据已被他人修改（版本号不一致），请刷新后重试。");
        }
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // 先取实体再复用对象删除逻辑，保证软删除与审计填充行为一致；不存在时静默忽略（幂等）
        var entity = await GetAsync(id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        await DeleteAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        // 重写领域仓储的物理删除为软删除：仅标记墓碑，默认查询自动过滤；
        // 走实体更新以填充审计字段并递增版本号
        entity.IsDeleted = true;
        await UpdateAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // 恢复软删除墓碑：直接按主键列更新（默认查询过滤不到已删除行，也不能走带版本校验的实体更新），幂等
        await Client.Updateable<TEntity>()
            .SetColumns(it => it.IsDeleted == false)
            .Where(it => it.Id == id && it.IsDeleted == true)
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task HardDeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await Client.Deleteable(entity).ExecuteCommandAsync(cancellationToken);
    }
}
