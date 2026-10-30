namespace Infrastructure.Repositories;

using System.Collections.Concurrent;
using System.Linq.Expressions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Paging;
using SqlSugar;

/// <summary>
/// 基于 SqlSugar 的领域仓储实现（对应 <see cref="DomainBase"/>）：
/// 实体表 / 关联表公共的查询统计、插入与物理删除。
/// <para>查询自动附加租户隔离过滤（全部实体），聚合根体系另有软删除过滤；
/// 列表查询支持多条件 AND 组合与多重排序（未指定排序时按创建时间倒序）；
/// 分页支持同样的动态条件与排序，未指定排序时按主键升序兜底，保证分页结果稳定。</para>
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端（单例 SqlSugarScope，内部按异步上下文隔离）。</param>
public class DomainRepository<TEntity>(ISqlSugarClient client) : IDomainRepository<TEntity>
    where TEntity : DomainBase, new()
{
    /// <summary>SqlSugar 客户端（单例 SqlSugarScope，内部按异步上下文隔离）。</summary>
    protected ISqlSugarClient Client => client;

    /// <summary>默认排序列名缓存（键为实体类型；分页排序表达式构建时复用）。</summary>
    private static readonly ConcurrentDictionary<Type, string> DefaultOrderColumnCache = new();

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await client.Queryable<TEntity>().InSingleAsync(id);
    }

    /// <inheritdoc />
    public virtual async Task<List<TEntity>> GetListAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
    {
        // 动态条件与排序拼接见 BuildDynamicQuery
        return await BuildDynamicQuery(predicates, orders).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetFirstAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await client.Queryable<TEntity>().Where(predicate).FirstAsync();
    }

    /// <inheritdoc />
    public virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await client.Queryable<TEntity>().AnyAsync(predicate);
    }

    /// <inheritdoc />
    public virtual async Task<PagedResult<TEntity>> GetPagedAsync(
        int pageIndex = 1,
        int pageSize = 20,
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
    {
        // 页参数越界收敛：页索引小于 1 修正为 1；页大小小于 1 回退默认 20，超过上限 500 收敛（防恶意大页查询）
        if (pageIndex < 1)
        {
            pageIndex = 1;
        }

        const int DefaultPageSize = 20;
        const int MaxPageSize = 500;
        pageSize = pageSize < 1 ? DefaultPageSize : pageSize > MaxPageSize ? MaxPageSize : pageSize;

        // 条件与排序规则同 GetListAsync：多条件 AND 组合；多重排序按字典顺序应用；
        // 未指定排序时不追加创建时间倒序，仅按主键升序排序（沿用原分页行为，保证分页结果稳定）
        var query = BuildDynamicQuery(predicates, orders, applyDefaultCreatedAtOrder: false);
        var total = new RefAsync<int>();
        var items = await query
            .OrderBy(BuildDefaultOrderSelector())
            .ToPageListAsync(pageIndex, pageSize, total);
        return PagedResult<TEntity>.Create(items, total.Value, pageIndex, pageSize);
    }

    /// <inheritdoc />
    public virtual async Task<long> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        // WhereIF：条件为 null 时跳过拼接
        return await client.Queryable<TEntity>()
            .WhereIF(predicate is not null, predicate!)
            .CountAsync();
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
    /// 构造默认排序表达式（升序）：有 Id 属性的实体（聚合根体系）按 Id 排序；
    /// 联合主键实体（无 Id 属性）按首个主键列排序，保证分页结果稳定。列名按类型缓存。
    /// </summary>
    /// <returns>排序表达式。</returns>
    protected Expression<Func<TEntity, object>> BuildDefaultOrderSelector()
    {
        var propertyName = DefaultOrderColumnCache.GetOrAdd(typeof(TEntity), type =>
        {
            if (type.GetProperty(nameof(AggregateBase.Id)) is not null)
            {
                return nameof(AggregateBase.Id);
            }

            // 无 Id 属性的联合主键实体：从实体元数据取首个主键列，兜底首个映射列
            var columns = client.EntityMaintenance.GetEntityInfo(type).Columns;
            var column = columns.FirstOrDefault(c => c.IsPrimarykey) ?? columns.FirstOrDefault()
                ?? throw new InvalidOperationException($"实体 {type.Name} 缺少可用于排序的列");
            return column.PropertyName;
        });

        var parameter = Expression.Parameter(typeof(TEntity), "it");
        var property = Expression.Property(parameter, propertyName);
        var converted = Expression.Convert(property, typeof(object));
        return Expression.Lambda<Func<TEntity, object>>(converted, parameter);
    }

    /// <summary>
    /// 构造动态查询：条件集合逐个 Where 拼接（SqlSugar 多次 Where 自动以 AND 组合，null 条目跳过）；
    /// 排序集合按字典顺序依次应用多重排序（value 为 true 升序、false 或 null 降序，null 条目跳过）。
    /// </summary>
    /// <param name="predicates">条件集合；null 或空集合表示不加过滤。</param>
    /// <param name="orders">排序集合；null 时不追加任何排序（由调用方决定默认排序）。</param>
    /// <param name="applyDefaultCreatedAtOrder">orders 为 null 时是否追加创建时间倒序默认排序；
    /// 列表查询传 true，分页查询传 false（由主键升序兜底保证分页稳定）。</param>
    /// <returns>已拼接条件与排序的查询对象。</returns>
    protected ISugarQueryable<TEntity> BuildDynamicQuery(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders,
        bool applyDefaultCreatedAtOrder = true)
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

        if (orders is not null)
        {
            foreach (var (selector, ascending) in orders)
            {
                if (selector is null)
                {
                    continue;
                }

                query = query.OrderBy(selector, ascending == true ? OrderByType.Asc : OrderByType.Desc);
            }
        }
        else if (applyDefaultCreatedAtOrder)
        {
            // 未指定排序时默认按创建时间倒序
            query = query.OrderByDescending(it => it.CreatedAt);
        }

        return query;
    }
}
