
using Domain.Shared.Enums;
using SqlSugar;

namespace Domain.Entities;
/// <summary>定时任务执行日志实体（由调度包装任务在执行前后写入，平台级数据不做租户隔离）。</summary>
[SugarIndex("ix_sys_job_log_created", nameof(CreatedAt), OrderByType.Desc)]
[SugarTable("sys_job_log", TableDescription = "定时任务执行日志实体（由调度包装任务在执行前后写入，平台级数据不做租户隔离）。")]
public class SysJobLog : AggregateBase
{
    /// <summary>任务 Id。</summary>
    [SugarColumn(ColumnDescription = "任务 Id。")]
    public Guid JobId { get; set; }

    /// <summary>任务名称。</summary>
    [SugarColumn(ColumnDescription = "任务名称。", Length = 50)]
    public string JobName { get; set; } = string.Empty;

    /// <summary>执行结果摘要。</summary>
    [SugarColumn(ColumnDescription = "执行结果摘要。", IsNullable = true, Length = 500)]
    public string? Result { get; set; }

    /// <summary>异常信息（失败时记录，截断 2000 字符）。</summary>
    [SugarColumn(ColumnDescription = "异常信息（失败时记录，截断 2000 字符）。", IsNullable = true, Length = 2000)]
    public string? Exception { get; set; }

    /// <summary>耗时（毫秒）。</summary>
    [SugarColumn(ColumnDescription = "耗时（毫秒）。")]
    public long CostMs { get; set; }

    /// <summary>执行状态（见 <see cref="Domain.Shared.Enums.ExecutionStatus"/>：1 成功，0 失败）。</summary>
    [SugarColumn(ColumnDescription = "执行状态（见 ExecutionStatus：1 成功，0 失败）。")]
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Success;

    /// <summary>触发时间。</summary>
    [SugarColumn(ColumnDescription = "触发时间。")]
    public DateTime TriggerTime { get; set; }
}
