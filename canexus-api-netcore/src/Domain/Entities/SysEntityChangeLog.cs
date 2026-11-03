
using SqlSugar;

namespace Domain.Entities;
/// <summary>实体变更日志实体：聚合根经聚合仓储更新时按字段记录 old / new 值（append-only，不参与业务）。</summary>
[SugarTable("sys_entity_change_log")]
public class SysEntityChangeLog : AggregateBase
{
    /// <summary>变更的实体类型名（如 SysUser）。</summary>
    [SugarColumn(Length = 128)]
    public string EntityName { get; set; } = string.Empty;

    /// <summary>变更的实体主键 Id。</summary>
    [SugarColumn]
    public Guid EntityId { get; set; }

    /// <summary>变更的字段名（实体属性名）。</summary>
    [SugarColumn(Length = 128)]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>变更前的值（null 表示原值为 NULL）。</summary>
    [SugarColumn(IsNullable = true, Length = 1000)]
    public string? OldValue { get; set; }

    /// <summary>变更后的值（null 表示新值为 NULL）。</summary>
    [SugarColumn(IsNullable = true, Length = 1000)]
    public string? NewValue { get; set; }

    /// <summary>变更时间（UTC，由变更记录器在更新成功后填充）。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? ChangedAt { get; set; }

    /// <summary>变更人（用户 Id）；无登录上下文（系统任务）时为 null。</summary>
    [SugarColumn(IsNullable = true)]
    public Guid? ChangedBy { get; set; }
}
