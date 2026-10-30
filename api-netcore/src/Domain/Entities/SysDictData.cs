namespace Domain.Entities;

using Domain.Shared.Enums;
using SqlSugar;

/// <summary>字典数据实体（同 dictType 下 label / value 由业务层保证唯一）。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成。</para></summary>
[SugarTable("sys_dict_data")]
public class SysDictData : AggregateBase
{
    /// <summary>所属字典类型。</summary>
    [SugarColumn(Length = 100)]
    public virtual string DictType { get; set; } = string.Empty;

    /// <summary>字典标签（显示名称）。</summary>
    [SugarColumn(Length = 100)]
    public virtual string DictLabel { get; set; } = string.Empty;

    /// <summary>字典键值（存储值）。</summary>
    [SugarColumn(Length = 100)]
    public virtual string DictValue { get; set; } = string.Empty;

    /// <summary>标签样式（见 <see cref="Domain.Shared.Enums.TagType"/>）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual TagType TagType { get; set; } = TagType.Primary;

    /// <summary>显示顺序（升序）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual int OrderNum { get; set; }

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 启用，0 停用）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;
}
