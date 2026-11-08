
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.Shared.Tenants;
using SqlSugar;

namespace Infrastructure.Repositories;
/// <summary>
/// 基于 SqlSugar 的树形聚合仓储实现（对应 <see cref="AggregateTreeBase{TAggregate}"/>）：
/// 在聚合仓储能力之上提供树形数据的查询与装配。
/// <para>子孙/祖先/建树基于「逐层广度优先查询 + 内存装配」实现（管理后台数据量级下开销可忽略），
/// 自动继承软删除与租户过滤。</para>
/// </summary>
/// <typeparam name="TAggregate">树形聚合类型。</typeparam>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端。</param>
/// <param name="currentUser">当前用户上下文。</param>
/// <param name="tenantContext">当前租户上下文（写入侧显式补偿租户条件）。</param>
public class AggregateTreeRepository<TAggregate>(ISqlSugarClient client, IUserContext currentUser, ITenantContext tenantContext) : AggregateRepository<TAggregate>(client, currentUser, tenantContext), IAggregateTreeRepository<TAggregate>
    where TAggregate : AggregateTreeBase<TAggregate>, new()
{
    /// <inheritdoc />
    public virtual async Task<List<TAggregate>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default)
    {
        return await Client.Queryable<TAggregate>()
            .Where(it => it.ParentId == parentId)
            .OrderBy(it => it.Id)
            .ToListAsync();
    }

    /// <inheritdoc />
    public virtual async Task<List<TAggregate>> GetDescendantsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        List<TAggregate> descendants = [];
        HashSet<Guid> visited = [id];
        List<Guid> currentIds = [id];

        // 广度优先逐层下探，visited 防御环状脏数据导致死循环
        while (currentIds.Count > 0)
        {
            var children = await Client.Queryable<TAggregate>()
                .Where(it => currentIds.Contains(it.ParentId))
                .OrderBy(it => it.Id)
                .ToListAsync();

            descendants.AddRange(children.Where(it => visited.Add(it.Id)));
            currentIds = [.. children.Select(it => it.Id).Where(visited.Contains)];
        }

        return descendants;
    }

    /// <inheritdoc />
    public virtual async Task<List<TAggregate>> GetAncestorsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        List<TAggregate> ancestors = [];
        HashSet<Guid> visited = [id];
        var node = await GetAsync([it => it.Id == id], cancellationToken);

        // 自父节点起逐级向上至根，visited 防御环状脏数据导致死循环
        while (node is not null && node.ParentId != Guid.Empty && visited.Add(node.ParentId))
        {
            node = await GetAsync([it => it.Id == node.ParentId], cancellationToken);
            if (node is not null)
            {
                ancestors.Add(node);
            }
        }

        return ancestors;
    }

    /// <inheritdoc />
    public virtual async Task<List<TAggregate>> GetTreeAsync(CancellationToken cancellationToken = default)
    {
        // 装配逻辑统一由聚合基类承接：同级按 OrderNum 升序、再按 Id 升序，父级缺失的孤儿节点挂到根级
        return AggregateTreeBase<TAggregate>.BuildTree(await GetListAsync(cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public virtual Task<bool> HasChildrenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return AnyAsync([it => it.ParentId == id], cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<bool> IsSelfOrDescendantAsync(Guid ancestorId, Guid potentialDescendantId, CancellationToken cancellationToken = default)
    {
        if (ancestorId == potentialDescendantId)
        {
            return true;
        }

        var descendants = await GetDescendantsAsync(ancestorId, cancellationToken);
        return descendants.Any(it => it.Id == potentialDescendantId);
    }

    /// <inheritdoc />
    public virtual async Task MoveAsync(Guid id, Guid newParentId, TreeMoveCodes? codes = null, CancellationToken cancellationToken = default)
    {
        codes ??= new TreeMoveCodes();

        if (newParentId == id)
        {
            throw new BusinessException(codes.ParentInvalid, $"{codes.EntityName}的父级不能选择自己");
        }

        var node = await GetAsync([it => it.Id == id], cancellationToken)
            ?? throw new BusinessException(codes.NodeNotFound, $"{codes.EntityName} {id} 不存在");

        // 父级未变更时幂等返回
        if (node.ParentId == newParentId)
        {
            return;
        }

        TAggregate? parent = null;
        if (newParentId != Guid.Empty)
        {
            // 环检测：新父级不可为自身或自己的后代（对所有树形聚合统一生效）
            if (await IsSelfOrDescendantAsync(id, newParentId, cancellationToken))
            {
                throw new BusinessException(codes.ParentInvalid, $"{codes.EntityName}的父级不能选择自己的下级");
            }

            parent = await GetAsync([it => it.Id == newParentId], cancellationToken)
                ?? throw new BusinessException(codes.ParentNotFound, $"{codes.EntityName}的父级节点 {newParentId} 不存在");
        }

        // 持久化交由 MoveCoreAsync：基础树仅改父级，带祖级路径的树形仓储重写它以联动刷新路径
        await MoveCoreAsync(node, parent, cancellationToken);
    }

    /// <summary>
    /// 执行父级变更的持久化（模板方法）：基类仅变更 ParentId；
    /// 需要额外持久化语义的树形仓储可重写本方法扩展。
    /// </summary>
    /// <param name="node">待移动的节点（已通过环检测与存在性校验）。</param>
    /// <param name="parent">新父节点；null 表示移动为根节点。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected virtual async Task MoveCoreAsync(TAggregate node, TAggregate? parent, CancellationToken cancellationToken)
    {
        node.ParentId = parent?.Id ?? Guid.Empty;
        await UpdateAsync(node, cancellationToken);
    }
}
