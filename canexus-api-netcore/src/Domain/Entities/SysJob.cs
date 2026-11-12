
using Domain.Shared.Enums;
using SqlSugar;

namespace Domain.Entities;
/// <summary>定时任务实体（平台级监控数据，不做租户隔离）。
/// <para>实体不保留验证逻辑与数据归一化：字段约束由 Application 层 FluentValidation Validators 承接，
/// trim 在字段映射阶段自动完成；Cron 表达式与任务类型的合法性依赖调度端口校验。</para></summary>
[SugarIndex("ix_sys_job_scope", nameof(DeletedAt), OrderByType.Asc, nameof(CreatedAt), OrderByType.Asc)]
[SugarTable("sys_job", TableDescription = "定时任务实体（平台级监控数据，不做租户隔离）。")]
public class SysJob : AggregateBase
{
    /// <summary>任务名称。</summary>
    [SugarColumn(ColumnDescription = "任务名称。", Length = 50)]
    public virtual string JobName { get; set; } = string.Empty;

    /// <summary>任务分组。</summary>
    [SugarColumn(ColumnDescription = "任务分组。", Length = 50)]
    public virtual string JobGroup { get; set; } = "DEFAULT";

    /// <summary>Cron 表达式（创建 / 更新时经调度端口校验）。</summary>
    [SugarColumn(ColumnDescription = "Cron 表达式（创建 / 更新时经调度端口校验）。", Length = 100)]
    public virtual string CronExpression { get; set; } = string.Empty;

    /// <summary>任务类型（已登记业务作业白名单标识，如内置示例 noop；不接受 CLR 程序集限定名）。</summary>
    [SugarColumn(ColumnDescription = "任务类型（已登记业务作业白名单标识，如内置示例 noop；不接受 CLR 程序集限定名）。", Length = 255)]
    public virtual string JobType { get; set; } = string.Empty;

    /// <summary>状态（见 <see cref="Domain.Shared.Enums.CommonStatus"/>：1 运行即入调度，0 暂停即出调度——启停语义在调度域的表现）。</summary>
    [SugarColumn(ColumnDescription = "状态（见 CommonStatus：1 运行即入调度，0 暂停即出调度——启停语义在调度域的表现）。")]
    public virtual CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>是否允许并发执行（false 时以 DisallowConcurrentExecution 包装器入调度）。</summary>
    [SugarColumn(ColumnDescription = "是否允许并发执行（false 时以 DisallowConcurrentExecution 包装器入调度）。")]
    public virtual bool Concurrent { get; set; }

    /// <summary>
    /// 作业参数（JSON 对象，仅扁平标量键值；由 <see cref="Domain.Shared.Jobs.JobParameters"/> 统一解析）。
    /// <para>入调度时逐项写入 Quartz 原生 <c>JobDataMap</c>，目标 IJob 从 <c>MergedJobDataMap</c> 读取；
    /// 不引入自研参数绑定 / 重试 / 超时框架——重试与恢复沿用 Quartz 的
    /// <c>RequestRecovery</c> / misfire 策略 / <c>CancellationToken</c>。</para>
    /// </summary>
    [SugarColumn(ColumnDescription = "作业参数（JSON 对象，扁平标量键值；写入 Quartz JobDataMap）。", IsNullable = true, Length = 2000)]
    public virtual string? ParametersJson { get; set; }
}
