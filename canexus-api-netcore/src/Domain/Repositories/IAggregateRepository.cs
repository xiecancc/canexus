
using System.Linq.Expressions;
using Domain.Entities;

namespace Domain.Repositories;
/// <summary>
/// 聚合仓储接口（对应 <see cref="AggregateBase"/>）：继承 <see cref="IDomainRepository{TEntity}"/>
/// （公共查询 / 插入），并补充聚合根专属的乐观并发更新、软删除与恢复能力。
/// <para>删除 / 恢复语义说明：删除一律为**软删除**（标记墓碑，默认查询自动过滤），恢复为解除墓碑；
/// 按主键 / 集合 / 条件重载统一为单条 SQL 按列更新——轮换版本令牌并显式填充更新人 / 更新时间
/// （按列更新绕过审计切面，由仓储兜底；版本轮换使旧令牌持有的实体更新失效，防止墓碑被复活；
/// 无逐行 CAS，删除 / 恢复自身不检测并发冲突）。
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
    /// <returns>更新成功返回 true（版本冲突时抛出业务异常而非返回 false）。</returns>
    public Task<bool> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>按主键软删除（标记 IsDeleted，单条 SQL 按列更新，仓储兜底填充更新人 / 更新时间并轮换版本令牌）；
    /// 主键不存在时静默忽略（幂等）。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响行数 &gt; 0 返回 true（不存在时为 false）。</returns>
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>按主键集合软删除：单条 SQL 按列更新（仓储兜底填充更新人 / 更新时间并轮换版本令牌）；
    /// 主键均不存在或均已删除时静默忽略（幂等）。</summary>
    /// <param name="ids">主键集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响行数 &gt; 0 返回 true。</returns>
    public Task<bool> DeleteAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>按条件恢复软删除的聚合（解除墓碑，单条 SQL 按列更新）；无匹配时静默忽略（幂等）。
    /// 注意：恢复为按列更新，仓储兜底填充更新人 / 更新时间并轮换版本令牌。</summary>
    /// <param name="predicate">恢复条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响行数 &gt; 0 返回 true。</returns>
    public Task<bool> RestoreAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>按主键恢复软删除的聚合（解除墓碑）；主键不存在或未删除时静默忽略（幂等）。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响行数 &gt; 0 返回 true。</returns>
    public Task<bool> RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>按主键集合恢复软删除的聚合（解除墓碑，单条 SQL）；主键均不存在或均未删除时静默忽略（幂等）。</summary>
    /// <param name="ids">主键集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响行数 &gt; 0 返回 true。</returns>
    public Task<bool> RestoreAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}
