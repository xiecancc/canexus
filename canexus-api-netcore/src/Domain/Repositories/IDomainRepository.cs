
using System.Linq.Expressions;
using Domain.Entities;
using Domain.Shared.Paging;

namespace Domain.Repositories;
/// <summary>
/// 领域仓储接口（对应 <see cref="DomainBase"/>）：实体表 / 关联表公共的查询统计、插入与物理删除能力。
/// <para>所有方法均为异步；查询自动附加租户隔离过滤（关联表与聚合根一致），
/// 聚合根体系另有软删除过滤；列表与分页查询支持多条件 AND 组合与多重排序
/// （orders 为 null 时默认按创建时间倒序），保证查询语义一致；
/// 列表 / 首条 / 分页另有泛型投影重载（SqlSugar Select&lt;TDto&gt; 按属性名自动映射返回指定类型）。
/// 聚合根的更新 / 软删除 / 恢复等完整能力见 <see cref="IAggregateRepository{TEntity}"/>。</para>
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
public interface IDomainRepository<TEntity>
    where TEntity : DomainBase
{
    /// <summary>按条件获取第一条实体（无匹配返回 null）。</summary>
    /// <param name="predicates">条件集合，各条件以 AND 组合后应用；null 或空集合表示不加过滤。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实体；不存在时返回 null。</returns>
    public Task<TEntity?> GetAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        CancellationToken cancellationToken = default);

    /// <summary>投影查询首条记录：支持多条件（AND 组合），结果按属性名自动映射到指定 DTO 类型，无匹配返回 null。</summary>
    /// <typeparam name="TDto">投影结果类型；属性名与实体匹配的字段自动赋值，未匹配字段为类型默认值。</typeparam>
    /// <param name="predicates">条件集合，各条件以 AND 组合后应用；null 或空集合表示不加过滤。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>投影结果；不存在时返回 null。</returns>
    public Task<TDto?> GetAsync<TDto>(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new();

    /// <summary>动态查询实体列表：支持多条件（AND 组合）与多重排序。</summary>
    /// <param name="predicates">条件集合，各条件以 AND 组合后应用；null 或空集合表示不加过滤。</param>
    /// <param name="orders">排序集合，按字典顺序依次应用多重排序；
    /// key 为排序字段表达式，value 为 true 升序、false 或 null 降序；null 或空集合时默认按创建时间倒序。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实体列表。</returns>
    public Task<List<TEntity>> GetListAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default);

    /// <summary>投影查询实体列表：支持多条件（AND 组合）与多重排序，结果按属性名自动映射到指定 DTO 类型
    /// （SqlSugar Select&lt;TDto&gt;，SQL 层只取 DTO 涉及的列，避免整实体回传）。</summary>
    /// <typeparam name="TDto">投影结果类型；属性名与实体匹配的字段自动赋值，未匹配字段为类型默认值。</typeparam>
    /// <param name="predicates">条件集合，各条件以 AND 组合后应用；null 或空集合表示不加过滤。</param>
    /// <param name="orders">排序集合，语义同 <see cref="GetListAsync"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>投影结果列表。</returns>
    public Task<List<TDto>> GetListAsync<TDto>(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new();

    /// <summary>分页查询：支持多条件（AND 组合）与多重排序；页参数由 <see cref="PagedRequest"/> 在 setter 中越界收敛。</summary>
    /// <param name="request">分页请求（PageIndex / PageSize 已收敛：页码从 1 起，页大小 1~500）。</param>
    /// <param name="predicates">条件集合，各条件以 AND 组合后应用；null 或空集合表示不加过滤。</param>
    /// <param name="orders">排序集合，按字典顺序依次应用多重排序；
    /// key 为排序字段表达式，value 为 true 升序、false 或 null 降序；null 或空集合时默认按创建时间倒序，主键升序作平局裁决保证分页稳定。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public Task<PagedResponse<TEntity>> GetPagedAsync(
        PagedRequest request,
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default);

    /// <summary>投影分页查询：条件与排序语义同 <see cref="GetPagedAsync"/>，结果按属性名自动映射到指定 DTO 类型
    /// （SqlSugar Select&lt;TDto&gt;，SQL 层只取 DTO 涉及的列）。</summary>
    /// <typeparam name="TDto">投影结果类型；属性名与实体匹配的字段自动赋值，未匹配字段为类型默认值。</typeparam>
    /// <param name="request">分页请求（PageIndex / PageSize 已收敛：页码从 1 起，页大小 1~500）。</param>
    /// <param name="predicates">条件集合，各条件以 AND 组合后应用；null 或空集合表示不加过滤。</param>
    /// <param name="orders">排序集合，按字典顺序依次应用多重排序；
    /// key 为排序字段表达式，value 为 true 升序、false 或 null 降序；null 或空集合时默认按创建时间倒序，主键升序作平局裁决保证分页稳定。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果（元素为投影类型）。</returns>
    public Task<PagedResponse<TDto>> GetPagedAsync<TDto>(
        PagedRequest request,
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new();

    /// <summary>判断是否存在满足条件的实体。</summary>
    /// <param name="predicates">条件集合，各条件以 AND 组合后应用；null 或空集合表示不加过滤。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>存在返回 true。</returns>
    public Task<bool> AnyAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        CancellationToken cancellationToken = default);

    /// <summary>统计实体数（条件集合 AND 组合过滤）。</summary>
    /// <param name="predicates">条件集合，各条件以 AND 组合后应用；null 或空集合表示全量统计。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>记录数。</returns>
    public Task<long> CountAsync(
        IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
        CancellationToken cancellationToken = default);

    /// <summary>插入实体；创建审计（创建时间 / 创建人）与租户归属由数据访问层自动填充，
    /// 聚合根的空主键与软删除标记亦由数据访问层自动初始化。
    /// 主键与审计字段就地填充到传入实体（返回 bool，调用方继续持有原实体引用即可获得插入后状态）。</summary>
    /// <param name="entity">实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>插入成功返回 true。</returns>
    public Task<bool> InsertAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>批量插入（单条 SQL 多值插入，避免循环逐条插入的 N+1 往返）。
    /// 审计填充等 Aop.DataExecuting 行为对每个实体同样生效。</summary>
    /// <param name="entities">实体集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>插入成功（受影响行数 &gt; 0）返回 true。</returns>
    public Task<bool> InsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>按条件物理删除（单条 DELETE，行从表中移除，不可恢复）。
    /// <para>语义说明：本方法在任何实体类型上都是物理删除，与软删除（<see cref="IAggregateRepository{TEntity}"/> 的
    /// <c>SoftDeleteAsync</c> 族，即墓碑标记）是两条显式分离的通道，不再按引用类型的静态类型隐式改写语义。
    /// 关联表 / 日志表等无墓碑语义的实体应使用本方法；聚合根默认应使用软删除，
    /// 仅在确需彻底清除数据（如合规擦除、测试夹具清理）时才调用本方法，并自行承担不可恢复与审计缺失的风险
    /// （物理删除不写字段级变更日志、不轮换版本令牌、不填充更新审计）。</para></summary>
    /// <param name="predicate">删除条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响行数 &gt; 0 返回 true。</returns>
    public Task<bool> HardDeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
}
