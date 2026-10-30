namespace Domain.Entities;

using SqlSugar;

/// <summary>系统参数配置实体（键值对形式的全局配置）。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成；键值不做 trim（允许任意内容）。</para></summary>
[SugarTable("sys_config")]
public class SysConfig : AggregateBase
{
    /// <summary>参数名称。</summary>
    [SugarColumn(Length = 100)]
    public virtual string ConfigName { get; set; } = string.Empty;

    /// <summary>参数键名（唯一，由业务层校验保证）。</summary>
    [SugarColumn(Length = 100)]
    public virtual string ConfigKey { get; set; } = string.Empty;

    /// <summary>参数键值。</summary>
    [SugarColumn(Length = 500)]
    public virtual string ConfigValue { get; set; } = string.Empty;

    /// <summary>是否内置参数（内置参数不允许删除）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual bool BuiltIn { get; set; }
}
