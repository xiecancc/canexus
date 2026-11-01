
using Domain.Entities;
using Domain.Repositories;
using Domain.Services;
using Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Infrastructure.Scheduling;
/// <summary>
/// 定时任务调度管理实现（Quartz）：JobKey 以「sys_job_{Id}」为名称、JobGroup 为分组，
/// 实际入调度的是包装任务（JobExecuteWrapper / DisallowConcurrent 变体），目标 IJob 类型由包装任务反射解析。
/// </summary>
/// <remarks>构造函数。</remarks>
public sealed class JobScheduleManager(ISchedulerFactory schedulerFactory, IAggregateRepository<SysJob> jobRepository, ILogger<JobScheduleManager> logger) : IJobScheduleManager
{
    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // 仅装载运行中（Status=1）任务；暂停任务不入调度（软删除由仓储过滤器自动排除）
        var jobs = await jobRepository.GetListAsync([j => j.Status == CommonStatus.Enabled], cancellationToken: cancellationToken);
        foreach (var job in jobs)
        {
            try
            {
                await ScheduleAsync(job, cancellationToken);
            }
            catch (Exception exception)
            {
                // 单个任务装载失败不影响其余任务与应用启动
                logger.LogError(exception, "装载定时任务失败：{JobName}（{JobType}）", job.JobName, job.JobType);
            }
        }

        if (jobs.Count > 0)
        {
            logger.LogInformation("定时任务：已装载 {Count} 个运行中任务", jobs.Count);
        }
    }

    /// <inheritdoc />
    public async Task ScheduleAsync(SysJob job, CancellationToken cancellationToken = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var jobKey = BuildJobKey(job.Id, job.JobGroup);
        var jobDetail = JobBuilder
            .Create(job.Concurrent ? typeof(JobExecuteWrapper) : typeof(JobExecuteWrapperDisallowConcurrent))
            .WithIdentity(jobKey)
            .WithDescription(job.JobName)
            .UsingJobData(JobExecuteWrapper.JobIdKey, job.Id.ToString())
            .StoreDurably()
            .Build();
        var trigger = TriggerBuilder
            .Create()
            .WithIdentity($"{jobKey.Name}_trigger", jobKey.Group)
            .WithCronSchedule(job.CronExpression)
            .Build();

        // 幂等替换：先移除旧定义（含旧触发器）再入调度
        await scheduler.DeleteJob(jobKey, cancellationToken);
        await scheduler.ScheduleJob(jobDetail, trigger, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid jobId, string jobGroup, CancellationToken cancellationToken = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await scheduler.DeleteJob(BuildJobKey(jobId, jobGroup), cancellationToken);
    }

    /// <inheritdoc />
    public async Task RunOnceAsync(SysJob job, CancellationToken cancellationToken = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var jobKey = BuildJobKey(job.Id, job.JobGroup);
        var jobDetail = JobBuilder
            .Create(job.Concurrent ? typeof(JobExecuteWrapper) : typeof(JobExecuteWrapperDisallowConcurrent))
            .WithIdentity(jobKey)
            .WithDescription(job.JobName)
            .UsingJobData(JobExecuteWrapper.JobIdKey, job.Id.ToString())
            .StoreDurably()
            .Build();

        // 暂停任务未在调度中：先确保 JobDetail 已存储，再立即触发一次（不影响 Cron 调度）
        await scheduler.AddJob(jobDetail, replace: true, cancellationToken);
        await scheduler.TriggerJob(jobKey, cancellationToken);
    }

    /// <summary>构造任务 JobKey（名称固定为 sys_job_{Id}，避免任务重命名导致调度失联）。</summary>
    private static JobKey BuildJobKey(Guid jobId, string jobGroup) => new($"sys_job_{jobId}", jobGroup);

    /// <inheritdoc />
    public bool IsValidCronExpression(string cronExpression) => CronExpression.IsValidExpression(cronExpression);

    /// <inheritdoc />
    public bool IsValidJobType(string jobType)
    {
        var targetType = Type.GetType(jobType, throwOnError: false);
        return targetType is not null && typeof(IJob).IsAssignableFrom(targetType);
    }
}
