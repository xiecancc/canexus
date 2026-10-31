
using System.Linq.Expressions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Paging;
using SqlSugar;

namespace Infrastructure.Repositories;
/// <summary>
/// 基于 SqlSugar 的领域仓储实现（对应 <see cref="DomainBase"/>）：
/// 实体表 / 关联表公共的查询统计、插入与物理删除。
/// <para>查询自动附加租户隔离过滤（全部实体），聚合根体系另有软删除过滤；
/// 列表查询支持多条件 AND 组合与多重排序（orders 为 null 时默认按创建时间倒序）；
/// 分页支持同样的动态条件与排序，排序键非唯一时调用方应在 orders 中显式追加唯一列保证分页稳定；
/// 列表 / 首条 / 分页另有泛型投影重载（Select&lt;TDto&gt; 按属性名自动映射，SQL 层只取 DTO 涉及的列）。</para>
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端（单例 SqlSugarScope，内部按异步上下文隔离）。</param>
public class DomainRepository<TEntity>(ISqlSugarClient client) : IDomainRepository<TEntity>
    where TEntity : DomainBase, new()
{
    /// <summary>SqlSugar 客户端（单例 SqlSugarScope，内部按异步上下文隔离）。</summary>
    protected ISqlSugarClient Client => client;

    /// <inheritdoc />
    public virtual async Task<List<TEntity>> GetListAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
    {
        // 条件拼接见 ApplyPredicates；排序拼接见 ApplyOrders（orders 为 null 时默认创建时间倒序）
        return await ApplyOrders(ApplyPredicates(predicates), orders).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        CancellationToken cancellationToken = default)
    {
        // 条件集合 AND 组合；走 ApplyPredicates，不追加排序
        return await ApplyPredicates(predicates).FirstAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<List<TDto>> GetListAsync<TDto>(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        // 条件拼接见 ApplyPredicates；Select<TDto> 按属性名自动映射，SQL 层只取 DTO 涉及的列
        return await ApplyOrders(ApplyPredicates(predicates), orders)
            .Select<TDto>()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<TDto?> GetAsync<TDto>(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        // 条件集合 AND 组合；Select<TDto> 按属性名自动映射，SQL 层只取 DTO 涉及的列
        return await ApplyPredicates(predicates)
            .Select<TDto>()
            .FirstAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<bool> AnyAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        CancellationToken cancellationToken = default)
    {
        // 条件集合 AND 组合，EXISTS 语义；走 ApplyPredicates 不追加排序
        return await ApplyPredicates(predicates).AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<PagedResult<TEntity>> GetPagedAsync(
        PagedRequest request,
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
    {
        // 页参数已由 PagedRequest 在 setter 中越界收敛；多条件 AND 组合（ApplyPredicates）；
        // 多重排序按字典顺序应用（ApplyOrders），orders 为 null 时默认创建时间倒序；
        // 排序键非唯一时调用方应在 orders 中显式追加唯一列（如 Id）保证分页稳定
        var query = ApplyOrders(ApplyPredicates(predicates), orders);
        var total = new RefAsync<int>();
        var items = await query
            .ToPageListAsync(request.PageIndex, request.PageSize, total, cancellationToken);
        return PagedResult<TEntity>.Create(items, total.Value, request);
    }

    /// <inheritdoc />
    public virtual async Task<PagedResult<TDto>> GetPagedAsync<TDto>(
        PagedRequest request,
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        // 页参数已由 PagedRequest 收敛；排序规则同实体版 GetPagedAsync：orders 为 null 时默认创建时间倒序；Select<TDto> 按属性名自动映射，SQL 层只取 DTO 涉及的列
        var query = ApplyOrders(ApplyPredicates(predicates), orders);
        var total = new RefAsync<int>();
        var items = await query
            .Select<TDto>()
            .ToPageListAsync(request.PageIndex, request.PageSize, total, cancellationToken);
        return PagedResult<TDto>.Create(items, total.Value, request);
    }

    /// <inheritdoc />
    public virtual async Task<long> CountAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        CancellationToken cancellationToken = default)
    {
        // 条件集合 AND 组合；null 或空集合表示全量统计；走 ApplyPredicates 不追加排序
        return await ApplyPredicates(predicates).CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        // 创建审计（创建时间 / 创建人）由 Aop.DataExecuting 自动填充；
        // 聚合根的空主键与软删除标记亦由其初始化，业务代码无需显式赋值
        return await client.Insertable(entity).ExecuteReturnEntityAsync();
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        // 物理删除：按主键（含联合主键）直接移除，不产生软删除墓碑；不存在时静默忽略（幂等）
        await client.Deleteable(entity).ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<int> DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        // 批量物理删除：单条 DELETE ... WHERE；聚合根软删除场景请勿使用本方法
        return await client.Deleteable<TEntity>()
            .Where(predicate)
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task InsertRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        // 批量插入：单条多值 INSERT；Aop.DataExecuting 对每个实体生效（审计 / 主键填充）
        await client.Insertable(entities.ToList()).ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>
    /// 拼接查询条件：条件集合逐个 Where 拼接（SqlSugar 多次 Where 自动以 AND 组合，null 条目跳过）。
    /// 供不关心排序的查询使用（首条 / Any / Count），避免被强加排序开销。
    /// </summary>
    /// <param name="predicates">条件集合；null 或空集合表示不加过滤。</param>
    /// <returns>已拼接条件的查询对象。</returns>
    protected ISugarQueryable<TEntity> ApplyPredicates(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates)
    {
        var query = client.Queryable<TEntity>();

        if (predicates is not null)
        {
            foreach (var predicate in predicates)
            {
                if (predicate is not null)
                {
                    query = query.Where(predicate);
                }
            }
        }

        return query;
    }

    /// <summary>
    /// 拼接排序条件：排序集合按字典顺序依次应用多重排序（value 为 true 升序、false 或 null 降序，null 条目跳过）；
    /// <paramref name="orders"/> 为 null 或空集合时默认按创建时间倒序（最新在前）。
    /// </summary>
    /// <param name="query">已拼接条件的查询对象。</param>
    /// <param name="orders">排序集合；null 或空集合时默认按创建时间倒序。</param>
    /// <returns>已拼接排序的查询对象。</returns>
    protected ISugarQueryable<TEntity> ApplyOrders(
        ISugarQueryable<TEntity> query,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders)
    {
        if (orders is not null && orders.Count > 0)
        {
            foreach (var (selector, ascending) in orders)
            {
                if (selector is null)
                {
                    continue;
                }

                query = query.OrderBy(selector, ascending == true ? OrderByType.Asc : OrderByType.Desc);
            }

            return query;
        }

        // 未指定排序时默认按创建时间倒序
        return query.OrderByDescending(it => it.CreatedAt);
    }
}
