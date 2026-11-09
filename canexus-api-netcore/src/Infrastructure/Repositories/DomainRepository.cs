
using System.Linq.Expressions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Paging;
using Infrastructure.Shared.Tenants;
using SqlSugar;

namespace Infrastructure.Repositories;
/// <summary>
/// 基于 SqlSugar 的领域仓储实现（对应 <see cref="DomainBase"/>）：
/// 实体表 / 关联表公共的查询统计、插入与物理删除（<see cref="HardDeleteAsync"/>）。
/// <para>查询自动附加租户隔离过滤（全部实体），聚合根体系另有软删除过滤；
/// 列表查询支持多条件 AND 组合与多重排序（orders 为 null 时默认按创建时间倒序）；
/// 分页支持同样的动态条件与排序，排序键非唯一时调用方应在 orders 中显式追加唯一列保证分页稳定；
/// 列表 / 首条 / 分页另有泛型投影重载（Select&lt;TDto&gt; 按属性名自动映射，SQL 层只取 DTO 涉及的列）。</para>
/// <para>增删改查统一经受保护的构建器方法 <see cref="BuildQuery"/> / <see cref="BuildInsert(TEntity)"/> /
/// <see cref="BuildUpdate(TEntity)"/> / <see cref="BuildDelete"/> 产出 SqlSugar 对象，
/// 基类方法与派生仓储复用同一入口；<see cref="BuildDelete"/> 为物理删除，软删除请走更新构建器按列更新。</para>
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端（单例 SqlSugarScope，内部按异步上下文隔离）。</param>
/// <param name="tenantContext">当前租户上下文（写入侧显式补偿租户条件）。</param>
public class DomainRepository<TEntity>(ISqlSugarClient client, ITenantContext tenantContext) : IDomainRepository<TEntity>
    where TEntity : DomainBase, new()
{
    /// <summary>SqlSugar 客户端（单例 SqlSugarScope，内部按异步上下文隔离）。</summary>
    protected ISqlSugarClient Client => client;

    /// <summary>当前租户 Id；null 表示平台范围（与全局查询过滤器的「平台可见全部」口径一致）。</summary>
    protected Guid? CurrentTenantId => tenantContext.TenantId;

    /// <inheritdoc />
    public virtual async Task<List<TEntity>> GetListAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
    {
        // 条件拼接见 BuildQuery；排序拼接见 ApplyOrders（orders 为 null 时默认创建时间倒序）
        return await ApplyOrders(BuildQuery(predicates), orders).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        CancellationToken cancellationToken = default)
    {
        // 条件集合 AND 组合；走 BuildQuery，不追加排序
        return await BuildQuery(predicates).FirstAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<List<TDto>> GetListAsync<TDto>(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        // 条件拼接见 BuildQuery；Select<TDto> 按属性名自动映射，SQL 层只取 DTO 涉及的列
        return await ApplyOrders(BuildQuery(predicates), orders)
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
        return await BuildQuery(predicates)
            .Select<TDto>()
            .FirstAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<bool> AnyAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        CancellationToken cancellationToken = default)
    {
        // 条件集合 AND 组合，EXISTS 语义；走 BuildQuery 不追加排序
        return await BuildQuery(predicates).AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<PagedResponse<TEntity>> GetPagedAsync(
        PagedRequest request,
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
    {
        // 页参数已由 PagedRequest 在 setter 中越界收敛；多条件 AND 组合（BuildQuery）；
        // 多重排序按字典顺序应用（ApplyOrders），orders 为 null 时默认创建时间倒序；
        // 排序键非唯一时调用方应在 orders 中显式追加唯一列（如 Id）保证分页稳定
        var query = ApplyOrders(BuildQuery(predicates), orders);
        var total = new RefAsync<int>();
        var items = await query
            .ToPageListAsync(request.PageIndex, request.PageSize, total, cancellationToken);
        return PagedResponse<TEntity>.Create(items, total.Value, request);
    }

    /// <inheritdoc />
    public virtual async Task<PagedResponse<TDto>> GetPagedAsync<TDto>(
        PagedRequest request,
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        // 页参数已由 PagedRequest 收敛；排序规则同实体版 GetPagedAsync：orders 为 null 时默认创建时间倒序；Select<TDto> 按属性名自动映射，SQL 层只取 DTO 涉及的列
        var query = ApplyOrders(BuildQuery(predicates), orders);
        var total = new RefAsync<int>();
        var items = await query
            .Select<TDto>()
            .ToPageListAsync(request.PageIndex, request.PageSize, total, cancellationToken);
        return PagedResponse<TDto>.Create(items, total.Value, request);
    }

    /// <inheritdoc />
    public virtual async Task<long> CountAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        CancellationToken cancellationToken = default)
    {
        // 条件集合 AND 组合；null 或空集合表示全量统计；走 BuildQuery 不追加排序
        return await BuildQuery(predicates).CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<bool> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        // 创建审计（创建时间 / 创建人）由 Aop.DataExecuting 自动填充；
        // 聚合根的空主键与软删除标记亦由其初始化，业务代码无需显式赋值；
        // ExecuteReturnEntityAsync 对非自增主键返回传入实体本身（审计切面就地填充），
        // 调用方继续持有的 entity 引用在插入后即携带主键与审计字段
        var inserted = await BuildInsert(entity).ExecuteReturnEntityAsync();
        return inserted is not null;
    }

    /// <inheritdoc />
    public virtual async Task<bool> HardDeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        // 批量物理删除：单条 DELETE ... WHERE，行被真正移除（无墓碑、无恢复，也不写字段级变更日志）。
        // 聚合根默认删除语义是软删除，请调用 AggregateRepository.SoftDeleteAsync；
        // 本方法仅在确需彻底清除（如合规擦除、测试夹具）时显式使用。
        // QueryFilter 只保证查询侧隔离（是否作用于 Deleteable 取决于 ORM 版本），写入侧显式补租户条件：
        // 平台上下文（null）保持全量，租户上下文只允许删本租户行。
        var delete = BuildDelete(predicate);
        if (CurrentTenantId is { } tenantId)
        {
            delete = delete.Where(it => it.TenantId == tenantId);
        }

        return await delete.ExecuteCommandAsync(cancellationToken) > 0;
    }

    /// <inheritdoc />
    public virtual async Task<bool> InsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        // 批量插入：单条多值 INSERT；Aop.DataExecuting 对每个实体生效（审计 / 主键填充）；
        // 空集合直接短路（SqlSugar Insertable 对空集合抛参数异常，语义上空插入即无操作）
        var materialized = entities as List<TEntity> ?? [.. entities];
        if (materialized.Count == 0)
        {
            return false;
        }

        return await BuildInsert(materialized).ExecuteCommandAsync(cancellationToken) > 0;
    }

    /// <summary>
    /// 构建查询对象（CRUD 构建器 · 查）：条件集合逐个 Where 拼接（SqlSugar 多次 Where 自动以 AND 组合，null 条目跳过），
    /// 返回可继续链式组合的查询对象；供首条 / 列表 / 分页 / Any / Count 及派生仓储复用。
    /// </summary>
    /// <param name="predicates">条件集合；null 或空集合表示不加过滤。</param>
    /// <returns>已拼接条件的查询对象。</returns>
    protected virtual ISugarQueryable<TEntity> BuildQuery(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null)
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

    /// <summary>构建单实体插入对象（CRUD 构建器 · 增）。</summary>
    /// <param name="entity">待插入实体。</param>
    /// <returns>SqlSugar 插入对象。</returns>
    protected virtual IInsertable<TEntity> BuildInsert(TEntity entity) => client.Insertable(entity);

    /// <summary>构建批量插入对象（CRUD 构建器 · 增；单条多值 INSERT）。</summary>
    /// <param name="entities">待插入实体集合。</param>
    /// <returns>SqlSugar 插入对象。</returns>
    protected virtual IInsertable<TEntity> BuildInsert(IEnumerable<TEntity> entities) => client.Insertable(entities.ToList());

    /// <summary>构建按实体更新对象（CRUD 构建器 · 改；聚合根在此基础上追加乐观锁）。</summary>
    /// <param name="entity">待更新实体。</param>
    /// <returns>SqlSugar 更新对象。</returns>
    protected virtual IUpdateable<TEntity> BuildUpdate(TEntity entity) => client.Updateable(entity);

    /// <summary>构建按列更新对象（CRUD 构建器 · 改；不携带实体，供 SetColumns 局部更新使用）。</summary>
    /// <returns>SqlSugar 更新对象。</returns>
    protected virtual IUpdateable<TEntity> BuildUpdate() => client.Updateable<TEntity>();

    /// <summary>
    /// 构建物理删除对象（CRUD 构建器 · 删）：直接执行 DELETE；
    /// 聚合根的软删除 / 恢复不走本方法，而是经构建器产出的更新对象按列更新（见 AggregateRepository）。
    /// </summary>
    /// <param name="predicate">删除条件。</param>
    /// <returns>SqlSugar 删除对象。</returns>
    protected virtual IDeleteable<TEntity> BuildDelete(Expression<Func<TEntity, bool>> predicate)
        => client.Deleteable<TEntity>().Where(predicate);

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
