
using SqlSugar;

namespace Domain.Entities;
/// <summary>字典类型实体。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成；字典类型唯一性由业务层校验保证。</para></summary>
[SugarTable("sys_dict_type", TableDescription = "字典类型实体。")]
public class SysDictType : AggregateBase
{
    /// <summary>字典名称。</summary>
    [SugarColumn(ColumnDescription = "字典名称。", Length = 100)]
    public virtual string DictName { get; set; } = string.Empty;

    /// <summary>字典类型（唯一，由业务层校验保证）。</summary>
    [SugarColumn(ColumnDescription = "字典类型（唯一，由业务层校验保证）。", Length = 100)]
    public virtual string DictType { get; set; } = string.Empty;
}
