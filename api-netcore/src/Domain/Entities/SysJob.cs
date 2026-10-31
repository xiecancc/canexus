
using Domain.Shared.Enums;
using SqlSugar;

namespace Domain.Entities;
/// <summary>定时任务实体（平台级监控数据，不做租户隔离）。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成；Cron 表达式与任务类型的合法性依赖调度端口校验。</para></summary>
[SugarTable("sys_job")]
public class SysJob : AggregateBase
{
    /// <summary>任务名称。</summary>
    [SugarColumn(Length = 50)]
    public virtual string JobName { get; set; } = string.Empty;

    /// <summary>任务分组。</summary>
    [SugarColumn(Length = 50)]
    public virtual string JobGroup { get; set; } = "DEFAULT";

    /// <summary>Cron 表达式（创建 / 更新时经调度端口校验）。</summary>
    [SugarColumn(Length = 100)]
    public virtual string CronExpression { get; set; } = string.Empty;

    /// <summary>任务类型（实现 Quartz.IJob 的程序集限定类型名，如内置示例 Infrastructure.Scheduling.NoOpJob, Infrastructure）。</summary>
    [SugarColumn(Length = 255)]
    public virtual string JobType { get; set; } = string.Empty;

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 运行即入调度，0 暂停即出调度——启停语义在调度域的表现）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>是否允许并发执行（false 时以 DisallowConcurrentExecution 包装器入调度）。</summary>
    [SugarColumn(IsNullable = true)]
    public virtual bool Concurrent { get; set; }
}
