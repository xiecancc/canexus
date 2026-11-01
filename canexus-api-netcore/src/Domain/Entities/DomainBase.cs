
using SqlSugar;

namespace Domain.Entities;
/// <summary>
/// 领域层顶层基类：承载全部实体共有的创建审计（创建时间 / 创建人）与租户归属。
/// <para>不含主键与软删除语义——主键由具体子类定义（聚合根为单主键 Id，
/// 关联表为业务字段的联合主键）；租户 Id 为空表示平台级数据；
/// 关联表删除为物理删除，聚合根删除为软删除。
/// 后续扩展领域事件时，在此基类中添加事件集合（收集）与出队/清空方法（分发）。</para>
/// </summary>
public abstract class DomainBase
{
    /// <summary>所属租户 Id；null 表示不归属任何租户（平台级数据）。插入时由数据访问层按当前租户上下文自动填充。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual Guid? TenantId { get; set; }

    /// <summary>创建时间，由数据访问层自动填充。</summary>
    public virtual DateTime CreatedAt { get; set; }

    /// <summary>创建人（用户 Id），无登录上下文时由数据访问层填充为 <see cref="Guid.Empty"/>。</summary>
    public virtual Guid CreatedBy { get; set; }

    /// <summary>
    /// 创建实体浅拷贝快照：供修改类领域事件在变更前捕获状态（成员级浅拷贝，
    /// 值类型 / 字符串 / Guid 属性独立；引用类型属性与原实例共享，事件为瞬态对象不构成风险）。
    /// </summary>
    /// <typeparam name="TEntity">实体自身类型（调用方传入实体精确类型，免强制转换）。</typeparam>
    /// <returns>变更前状态快照。</returns>
    public TEntity Snapshot<TEntity>() where TEntity : DomainBase
    {
        return (TEntity)MemberwiseClone();
    }
}
