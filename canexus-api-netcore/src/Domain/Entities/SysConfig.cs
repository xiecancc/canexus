
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using SqlSugar;

namespace Domain.Entities;
/// <summary>系统参数配置实体（键值对形式的全局配置）。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成；键值不做 trim（允许任意内容）。</para>
/// <para>聚合不变量（内置参数删除保护）以守卫方法内聚于本实体，命令处理器不得散落复刻。</para></summary>
[SugarTable("sys_config", TableDescription = "系统参数配置实体（键值对形式的全局配置）。")]
public class SysConfig : AggregateBase
{
    /// <summary>参数名称。</summary>
    [SugarColumn(ColumnDescription = "参数名称。", Length = 100)]
    public virtual string ConfigName { get; set; } = string.Empty;

    /// <summary>参数键名（唯一，由业务层校验保证）。</summary>
    [SugarColumn(ColumnDescription = "参数键名（唯一，由业务层校验保证）。", Length = 100)]
    public virtual string ConfigKey { get; set; } = string.Empty;

    /// <summary>参数键值。</summary>
    [SugarColumn(ColumnDescription = "参数键值。", Length = 500)]
    public virtual string ConfigValue { get; set; } = string.Empty;

    /// <summary>是否内置参数（内置参数不允许删除）。</summary>
    [SugarColumn(ColumnDescription = "是否内置参数（内置参数不允许删除）。")]
    public virtual bool BuiltIn { get; set; }

    /// <summary>聚合不变量守卫：内置参数拒绝删除。</summary>
    /// <exception cref="BusinessException">当前参数为内置参数时抛出（错误码 <see cref="ErrorCodes.ConfigBuiltInProtected"/>）。</exception>
    public void EnsureDeletable()
    {
        if (BuiltIn)
        {
            throw new BusinessException(ErrorCodes.ConfigBuiltInProtected, "内置参数不允许删除");
        }
    }
}
