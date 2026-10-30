namespace Domain.Entities;

using SqlSugar;

/// <summary>
/// 树形聚合根基类：在 <see cref="AggregateBase"/> 之上提供树形结构的统一建模。
/// <para>持久化字段仅 <see cref="ParentId"/>（Guid.Empty 表示根节点）；
/// <see cref="Parent"/> 与 <see cref="Children"/> 为内存导航属性，不映射到数据库，
/// 由 <c>IAggregateTreeRepository&lt;TAggregate&gt;</c> 的树形查询装配。</para>
/// </summary>
/// <typeparam name="TAggregate">具体聚合类型（CRTP 自约束，保证导航属性为具体聚合类型）。</typeparam>
public abstract class AggregateTreeBase<TAggregate> : AggregateBase
    where TAggregate : AggregateTreeBase<TAggregate>
{
    /// <summary>根节点父 Id（Guid.Empty 表示无父级）。</summary>
    public static readonly Guid RootParentId = Guid.Empty;

    /// <summary>父节点 Id；Guid.Empty 表示根节点。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual Guid ParentId { get; set; }

    /// <summary>父节点引用（内存导航属性，不持久化；根节点为 null）。</summary>
    [SugarColumn(IsIgnore = true)]
    public virtual TAggregate? Parent { get; set; }

    /// <summary>子节点集合（内存导航属性，不持久化），由树形查询装配。</summary>
    [SugarColumn(IsIgnore = true)]
    public virtual List<TAggregate> Children { get; set; } = [];

    /// <summary>同级排序依据：显示顺序（升序），由具体树形聚合声明持久化属性。</summary>
    public abstract int OrderNum { get; set; }

    /// <summary>
    /// 将节点集合装配为树（纯内存逻辑，供仓储建树与查询处理器过滤后建树复用）：
    /// <list type="bullet">
    /// <item>按 ParentId 递归挂载，<see cref="Children"/> 全量填充；</item>
    /// <item>同级按 <see cref="OrderNum"/> 升序、再按 Id 升序；</item>
    /// <item>父节点不在集合内的节点（ParentId != <see cref="RootParentId"/> 且父级缺失）挂到根级，
    /// 避免数据脏或过滤场景下节点丢失。</item>
    /// </list>
    /// </summary>
    /// <param name="nodes">节点集合（全量或已过滤；为提升性能不做去重防御，调用方保证 Id 唯一）。</param>
    /// <returns>根级节点列表（Children 内含全部后代；Parent 导航不回填）。</returns>
    public static List<TAggregate> BuildTree(IEnumerable<TAggregate> nodes)
    {
        var list = nodes.ToList();
        var ids = list.Select(n => n.Id).ToHashSet();

        // 每个父 Id 分组后先行排序，保证任意层级同级顺序一致（OrderNum 升序 → Id 升序）
        var childrenMap = list
            .GroupBy(n => n.ParentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.OrderNum).ThenBy(n => n.Id).ToList());

        foreach (var node in list)
        {
            node.Children = childrenMap.TryGetValue(node.Id, out var children) ? children : [];
        }

        // 根级 = 根节点（ParentId 为 Guid.Empty）与父级缺失的孤儿节点；其余挂到父节点下
        return list
            .Where(n => n.ParentId == RootParentId || !ids.Contains(n.ParentId))
            .OrderBy(n => n.OrderNum).ThenBy(n => n.Id)
            .ToList();
    }
}
