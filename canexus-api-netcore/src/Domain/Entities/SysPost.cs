
using Domain.Shared.Enums;
using SqlSugar;

namespace Domain.Entities;
/// <summary>岗位实体：组织内的职务岗位档案（如董事长、项目经理），供用户管理页关联分配。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成；岗位编码唯一性由业务层校验保证。</para></summary>
[SugarTable("sys_post", TableDescription = "岗位实体：组织内职务岗位档案")]
public class SysPost : AggregateBase
{
    /// <summary>岗位名称。</summary>
    [SugarColumn(ColumnDescription = "岗位名称。", Length = 50)]
    public virtual string PostName { get; set; } = string.Empty;

    /// <summary>岗位编码（唯一，由业务层校验保证）。</summary>
    [SugarColumn(ColumnDescription = "岗位编码（唯一，由业务层校验保证）。", Length = 64)]
    public virtual string PostCode { get; set; } = string.Empty;

    /// <summary>显示顺序（升序）。</summary>
    [SugarColumn(ColumnDescription = "显示顺序（升序）。")]
    public virtual int OrderNum { get; set; }

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 启用，0 停用；停用岗位不出现在用户分配下拉中）。</summary>
    [SugarColumn(ColumnDescription = "状态（1 启用，0 停用）。")]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;
}
