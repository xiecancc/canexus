
using Domain.Shared.Enums;
using SqlSugar;

namespace Domain.Entities;
/// <summary>系统部门实体（树形结构）。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成；树形结构由 ParentId 与树形仓储表达。</para></summary>
[SugarIndex("ix_sys_dept_parent", nameof(ParentId), OrderByType.Asc)]
[SugarIndex("ix_sys_dept_scope", nameof(TenantId), OrderByType.Asc, nameof(DeletedAt), OrderByType.Asc, nameof(CreatedAt), OrderByType.Asc)]
[SugarTable("sys_dept", TableDescription = "系统部门实体（树形结构）。")]
public class SysDept : AggregateTreeBase<SysDept>
{
    /// <summary>部门名称。</summary>
    [SugarColumn(ColumnDescription = "部门名称。", Length = 30)]
    public virtual string DeptName { get; set; } = string.Empty;

    /// <summary>显示顺序（升序，树形同级排序依据）。</summary>
    [SugarColumn(ColumnDescription = "显示顺序（升序，树形同级排序依据）。")]
    public override int OrderNum { get; set; }

    /// <summary>负责人。</summary>
    [SugarColumn(ColumnDescription = "负责人。", IsNullable = true, Length = 20)]
    public virtual string? Leader { get; set; }

    /// <summary>联系电话。</summary>
    [SugarColumn(ColumnDescription = "联系电话。", IsNullable = true, Length = 20)]
    public virtual string? Phone { get; set; }

    /// <summary>邮箱。</summary>
    [SugarColumn(ColumnDescription = "邮箱。", IsNullable = true, Length = 50)]
    public virtual string? Email { get; set; }

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 启用，0 停用）。</summary>
    [SugarColumn(ColumnDescription = "状态（见 CommonStatus：1 启用，0 停用）。")]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;
}
