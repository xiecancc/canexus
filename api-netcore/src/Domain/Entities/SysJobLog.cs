namespace Domain.Entities;

using SqlSugar;
using Domain.Shared.Enums;

/// <summary>定时任务执行日志实体（由调度包装任务在执行前后写入，平台级数据不做租户隔离）。</summary>
[SugarTable("sys_job_log")]
public class SysJobLog : AggregateBase
{
    /// <summary>任务 Id。</summary>
    [SugarColumn(IsNullable = true)]
    public Guid JobId { get; set; }

    /// <summary>任务名称。</summary>
    [SugarColumn(Length = 50)]
    public string JobName { get; set; } = string.Empty;

    /// <summary>执行结果摘要。</summary>
    [SugarColumn(IsNullable = true, Length = 500)]
    public string? Result { get; set; }

    /// <summary>异常信息（失败时记录，截断 2000 字符）。</summary>
    [SugarColumn(IsNullable = true, Length = 2000)]
    public string? Exception { get; set; }

    /// <summary>耗时（毫秒）。</summary>
    [SugarColumn(IsNullable = true)]
    public long CostMs { get; set; }

    /// <summary>执行状态（见 <see cref="Domain.Shared.Enums.ExecutionStatus"/>：1 成功，0 失败）。</summary>
    [SugarColumn(IsNullable = true)]
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Success;

    /// <summary>触发时间。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime TriggerTime { get; set; }
}
