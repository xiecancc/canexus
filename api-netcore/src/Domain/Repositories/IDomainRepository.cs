namespace Domain.Repositories;

using System.Linq.Expressions;
using Domain.Entities;
using Domain.Shared.Paging;

/// <summary>
/// 领域仓储接口（对应 <see cref="DomainBase"/>）：实体表 / 关联表公共的查询统计、插入与物理删除能力。
/// <para>所有方法均为异步；查询自动附加租户隔离过滤（关联表与聚合根一致），
/// 聚合根体系另有软删除过滤；列表与分页查询支持多条件 AND 组合与多重排序
/// （未指定排序时按创建时间倒序），保证查询语义一致。
/// 聚合根的更新 / 软删除 / 恢复等完整能力见 <see cref="IAggregateRepository{TEntity}"/>。</para>
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
public interface IDomainRepository<TEntity>
    where TEntity : DomainBase
{
    /// <summary>按主键获取实体（聚合根为 Id 主键；联合主键实体请使用条件查询）。</summary>
    /// <param name="id">主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实体；不存在时返回 null。</returns>
    Task<TEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>动态查询实体列表：支持多条件（AND 组合）与多重排序。</summary>
    /// <param name="predicates">条件集合，各条件以 AND 组合后应用；null 或空集合表示不加过滤。</param>
    /// <param name="orders">排序集合，按字典顺序依次应用多重排序；
    /// key 为排序字段表达式，value 为 true 升序、false 或 null 降序；null 时默认按创建时间倒序。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实体列表。</returns>
    Task<List<TEntity>> GetListAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default);

    /// <summary>按条件获取第一条实体（无匹配返回 null）。</summary>
    /// <param name="predicate">查询条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实体；不存在时返回 null。</returns>
    Task<TEntity?> GetFirstAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>判断是否存在满足条件的实体。</summary>
    /// <param name="predicate">查询条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>存在返回 true。</returns>
    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>分页查询：支持多条件（AND 组合）与多重排序；页索引从 1 开始，页大小越界自动收敛。</summary>
    /// <param name="pageIndex">页索引，从 1 开始；小于 1 时自动修正为 1。</param>
    /// <param name="pageSize">每页条数；小于 1 时回退默认值 20，超过上限 500 时收敛为 500（防止恶意大页查询）。</param>
    /// <param name="predicates">条件集合，各条件以 AND 组合后应用；null 或空集合表示不加过滤。</param>
    /// <param name="orders">排序集合，按字典顺序依次应用多重排序；
    /// key 为排序字段表达式，value 为 true 升序、false 或 null 降序；null 时按主键升序排序保证分页稳定。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    Task<PagedResult<TEntity>> GetPagedAsync(
        int pageIndex = 1,
        int pageSize = 20,
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default);

    /// <summary>统计实体数（可选条件过滤，配合 WhereIF 按需拼接）。</summary>
    /// <param name="predicate">查询条件；null 表示全量统计。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>记录数。</returns>
    Task<long> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);

    /// <summary>插入实体；创建审计（创建时间 / 创建人）与租户归属由数据访问层自动填充，
    /// 聚合根的空主键与软删除标记亦由数据访问层自动初始化。</summary>
    /// <param name="entity">实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>插入后的实体。</returns>
    Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>物理删除实体（直接从数据库移除，不产生软删除墓碑），不存在时静默忽略（幂等）。
    /// 注意：聚合根请改用 <see cref="IAggregateRepository{TEntity}"/>，其删除为软删除。</summary>
    /// <param name="entity">实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>按条件批量物理删除（单条 SQL，避免循环逐条删除的 N+1 往返）。
    /// 典型场景：关联表全量替换时按所属键清除旧关联。注意为物理删除语义。</summary>
    /// <param name="predicate">删除条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响的行数。</returns>
    Task<int> DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>批量插入（单条 SQL 多值插入，避免循环逐条插入的 N+1 往返）。
    /// 审计填充等 Aop.DataExecuting 行为对每个实体同样生效。</summary>
    /// <param name="entities">实体集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task InsertRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
}
