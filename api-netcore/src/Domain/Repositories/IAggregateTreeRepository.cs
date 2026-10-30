namespace Domain.Repositories;

using Domain.Entities;
using Domain.Shared.Results;

/// <summary>
/// 树形聚合仓储接口（对应 <see cref="AggregateTreeBase{TAggregate}"/>）：
/// 在聚合仓储能力之上提供树形数据的查询与装配。
/// <para>树形方法均以内存装配方式实现（数据量为管理后台量级），
/// 返回的节点 <see cref="AggregateTreeBase{TAggregate}.Parent"/> /
/// <see cref="AggregateTreeBase{TAggregate}.Children"/> 导航属性会被正确填充。</para>
/// </summary>
/// <typeparam name="TAggregate">树形聚合类型。</typeparam>
public interface IAggregateTreeRepository<TAggregate> : IAggregateRepository<TAggregate>
    where TAggregate : AggregateTreeBase<TAggregate>
{
    /// <summary>获取指定节点的直接子节点（一层，不含孙级），按主键升序。</summary>
    /// <param name="parentId">父节点 Id；Guid.Empty 表示获取全部根节点。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>子节点列表。</returns>
    Task<List<TAggregate>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);

    /// <summary>获取指定节点的全部子孙节点（所有层级，不含自身），按主键升序。</summary>
    /// <param name="id">节点 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>子孙节点列表（平铺）。</returns>
    Task<List<TAggregate>> GetDescendantsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>获取指定节点的祖先链（自父节点起逐级向上至根，不含自身），按层级由近及远排序。</summary>
    /// <param name="id">节点 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>祖先节点列表；节点为根或不存在时返回空列表。</returns>
    Task<List<TAggregate>> GetAncestorsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>加载全量数据并装配为树（装配规则由 <see cref="AggregateTreeBase{TAggregate}.BuildTree"/> 统一承接），
    /// 返回根节点列表：同级按 OrderNum 升序、再按 Id 升序；父级缺失的孤儿节点挂到根级。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>根节点列表（Children 内含全部后代；Parent 导航不回填）。</returns>
    Task<List<TAggregate>> GetTreeAsync(CancellationToken cancellationToken = default);

    /// <summary>判断指定节点是否存在直接子节点。</summary>
    /// <param name="id">节点 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>存在子节点返回 true。</returns>
    Task<bool> HasChildrenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>判断 potentialDescendantId 是否为 ancestorId 的自身或子孙节点（用于环检测与数据范围校验）。</summary>
    /// <param name="ancestorId">候选祖先节点 Id。</param>
    /// <param name="potentialDescendantId">候选后代节点 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是自身或后代返回 true。</returns>
    Task<bool> IsSelfOrDescendantAsync(Guid ancestorId, Guid potentialDescendantId, CancellationToken cancellationToken = default);

    /// <summary>移动节点到新父级（所有树形聚合通用的父级变更能力）：
    /// 校验新父级合法（不可为自身或自身后代）且存在后变更 ParentId；
    /// 带祖级路径的树形聚合仓储（如部门）同时重算自身祖级路径并联动刷新全部后代路径；
    /// 父级未变更时幂等返回。多次写库的原子性由调用方通过 <c>IUnitOfWork</c> 事务保障。</summary>
    /// <param name="id">待移动的节点 Id。</param>
    /// <param name="newParentId">新父节点 Id；Guid.Empty 表示移动为根节点。</param>
    /// <param name="codes">业务错误码与实体名定制；null 时使用通用段缺省值，模块调用方可传入自己的分段码保持 API 契约稳定。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task MoveAsync(Guid id, Guid newParentId, TreeMoveCodes? codes = null, CancellationToken cancellationToken = default);
}
