
using System.Linq.Expressions;
using Domain.Entities;

namespace Domain.Repositories;
/// <summary>
/// 聚合仓储接口（对应 <see cref="AggregateBase"/>）：继承 <see cref="IDomainRepository{TEntity}"/>
/// （公共查询 / 插入 / 物理删除），并补充聚合根专属的乐观并发更新、软删除与恢复能力。
/// <para>删除语义说明：本接口的 <c>SoftDeleteAsync</c> 族一律为**软删除**（写入 DeletedAt 时间戳墓碑，
/// 默认查询自动过滤），恢复为把 DeletedAt 置回 null；按主键 / 集合 / 条件重载统一走
/// <c>ExecuteCommandWithOptLock</c> 按列更新，与 <c>UpdateAsync</c> 共用同一套框架级乐观并发：
/// 携带 expectedVersion 时以其作 CAS 条件（陈旧令牌命中 0 行即抛冲突），成功后自动轮换版本令牌，
/// 使旧快照的实体更新失效，防止墓碑被复活。
/// 基接口继承的 <see cref="IDomainRepository{TEntity}.HardDeleteAsync"/> 仍是物理删除，两者并存不互相改写：
/// 需要软删除的子类调用 <c>SoftDeleteAsync</c>，确需彻底清除时显式调用 <c>HardDeleteAsync</c>。</para>
/// <para>审计字段（创建/更新人与时间、软删除时间戳）与空 Guid 主键由数据访问层自动填充；
/// 更新基于 <see cref="AggregateBase.Version"/> 做乐观并发控制，版本冲突时抛出业务异常。</para>
/// </summary>
/// <typeparam name="TEntity">聚合根实体类型。</typeparam>
public interface IAggregateRepository<TEntity> : IDomainRepository<TEntity>
    where TEntity : AggregateBase
{
    /// <summary>按主键读取软删除墓碑（绕过软删除过滤，按当前租户范围约束）；不存在或未被软删除时返回 null。
    /// <para>供恢复类写操作在解除墓碑前做数据范围 / 越权准入校验（通用查询看不到墓碑，不能复用 GetAsync）。</para></summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>软删除的聚合墓碑；不存在或未删除时为 null。</returns>
    public Task<TEntity?> GetDeletedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>更新聚合（全字段更新）；审计字段由框架自动填充，
    /// 并基于版本号做乐观并发校验——版本不一致时抛出并发冲突业务异常。</summary>
    /// <param name="entity">聚合（其 <see cref="AggregateBase.Version"/> 须为读取时的值）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新成功返回 true（版本冲突时抛出业务异常而非返回 false）。</returns>
    public Task<bool> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>按主键软删除（写入 DeletedAt 时间戳，经 OptLock 按列更新并自动轮换版本令牌）；
    /// 主键不存在时静默忽略（幂等）。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="expectedVersion">客户端提交的乐观锁版本令牌；非 null 时追加 Version CAS 条件，命中 0 行抛并发冲突；null 保持幂等静默忽略语义。</param>
    /// <returns>受影响行数 &gt; 0 返回 true（不存在时为 false）。</returns>
    public Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default, Guid? expectedVersion = null);

    /// <summary>按主键集合软删除：逐行经 OptLock 按列更新并轮换版本令牌；
    /// 主键均不存在或均已删除时静默忽略（幂等）。</summary>
    /// <param name="ids">主键集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="expectedVersion">客户端提交的乐观锁版本令牌；非 null 时追加 Version CAS 条件，命中 0 行抛并发冲突；null 保持幂等静默忽略语义。</param>
    /// <returns>受影响行数 &gt; 0 返回 true。</returns>
    public Task<bool> SoftDeleteAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default, Guid? expectedVersion = null);

    /// <summary>按条件软删除：逐行经 OptLock 按列更新并轮换版本令牌；
    /// 无匹配或均已是墓碑时静默忽略（幂等）。</summary>
    /// <param name="predicate">筛选条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="expectedVersion">客户端提交的乐观锁版本令牌；非 null 时追加 Version CAS 条件，命中 0 行抛并发冲突；null 保持幂等静默忽略语义。</param>
    /// <returns>受影响行数 &gt; 0 返回 true。</returns>
    public Task<bool> SoftDeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default, Guid? expectedVersion = null);

    /// <summary>按条件恢复软删除的聚合（DeletedAt 置回 null）；无匹配时静默忽略（幂等）。
    /// 注意：恢复同样经 OptLock 按列更新并轮换版本令牌。</summary>
    /// <param name="predicate">恢复条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="expectedVersion">客户端提交的乐观锁版本令牌；非 null 时追加 Version CAS 条件，命中 0 行抛并发冲突；null 保持幂等静默忽略语义。</param>
    /// <returns>受影响行数 &gt; 0 返回 true。</returns>
    public Task<bool> RestoreAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default, Guid? expectedVersion = null);

    /// <summary>按主键恢复软删除的聚合（解除墓碑）；主键不存在或未删除时静默忽略（幂等）。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="expectedVersion">客户端提交的乐观锁版本令牌；非 null 时追加 Version CAS 条件，命中 0 行抛并发冲突；null 保持幂等静默忽略语义。</param>
    /// <returns>受影响行数 &gt; 0 返回 true。</returns>
    public Task<bool> RestoreAsync(Guid id, CancellationToken cancellationToken = default, Guid? expectedVersion = null);

    /// <summary>按主键集合恢复软删除的聚合（DeletedAt 置回 null）；主键均不存在或均未删除时静默忽略（幂等）。</summary>
    /// <param name="ids">主键集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="expectedVersion">客户端提交的乐观锁版本令牌；非 null 时追加 Version CAS 条件，命中 0 行抛并发冲突；null 保持幂等静默忽略语义。</param>
    /// <returns>受影响行数 &gt; 0 返回 true。</returns>
    public Task<bool> RestoreAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default, Guid? expectedVersion = null);
}
