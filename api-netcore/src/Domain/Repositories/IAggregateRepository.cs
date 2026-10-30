namespace Domain.Repositories;

using Domain.Entities;

/// <summary>
/// 聚合仓储接口（对应 <see cref="AggregateBase"/>）：继承 <see cref="IDomainRepository{TEntity}"/>
/// （公共查询 / 插入 / 物理删除），并补充聚合根专属的乐观并发更新、软删除与恢复能力。
/// <para>注意：本接口将继承而来的 <see cref="IDomainRepository{TEntity}.DeleteAsync(TEntity, CancellationToken)"/>
/// 语义收窄为软删除（标记墓碑，默认查询自动过滤）；实现类必须重写为软删除，物理删除走 <see cref="HardDeleteAsync"/>。
/// 审计字段（创建/更新人与时间、软删除标记）与空 Guid 主键由数据访问层自动填充；
/// 更新基于 <see cref="AggregateBase.Version"/> 做乐观并发控制，版本冲突时抛出业务异常。</para>
/// </summary>
/// <typeparam name="TEntity">聚合根实体类型。</typeparam>
public interface IAggregateRepository<TEntity> : IDomainRepository<TEntity>
    where TEntity : AggregateBase
{
    /// <summary>更新聚合（全字段更新）；审计字段由框架自动填充，
    /// 并基于版本号做乐观并发校验——版本不一致时抛出并发冲突业务异常。</summary>
    /// <param name="entity">聚合（其 <see cref="AggregateBase.Version"/> 须为读取时的值）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>按主键删除；自动转为软删除（标记 IsDeleted），主键不存在时静默忽略（幂等）。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>删除聚合（重写领域仓储的物理删除语义）：自动转为软删除
    /// （标记 IsDeleted，审计字段由框架自动填充并递增版本号）。</summary>
    /// <param name="entity">聚合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>恢复软删除的聚合（标记 IsDeleted = false）；主键不存在或未删除时静默忽略（幂等）。
    /// 注意：恢复操作绕过默认查询过滤与版本校验，不递增版本号。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>物理删除聚合（直接从数据库移除，不走软删除墓碑），不存在时静默忽略（幂等）。
    /// 仅建议用于日志等无需回收站语义的场景。</summary>
    /// <param name="entity">聚合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task HardDeleteAsync(TEntity entity, CancellationToken cancellationToken = default);
}
