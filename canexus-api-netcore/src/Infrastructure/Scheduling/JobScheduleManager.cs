
using Domain.Entities;
using Domain.Repositories;
using Domain.Services;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Jobs;
using Domain.Shared.Results;
using Infrastructure.Shared.Locking;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;

namespace Infrastructure.Scheduling;
/// <summary>
/// 定时任务调度管理实现（Quartz）：JobKey 以「sys_job_{Id}」为名称、JobGroup 为分组，
/// 实际入调度的是包装任务（JobExecuteWrapper / DisallowConcurrent 变体），目标 IJob 类型由包装任务经白名单注册表解析；
/// JobDataMap 携带业务任务 Id 与作业参数（T5 #4），重试/恢复/misfire 全部沿用 Quartz 原生能力。
/// <para>同步策略（P1-C）：启动装载与单任务入调度均采用「差异比对后 add / update / delete」，不再无条件
/// DeleteJob + ScheduleJob；定义未变化时保留既有 Trigger（不重置下次触发时间），仅 Cron 变更时 RescheduleJob。
/// 启动同步由持有分布式锁的实例执行（滚动发布时其余副本跳过，持锁副本完成全量同步）。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="schedulerFactory">Quartz 调度器工厂。</param>
/// <param name="jobRepository">任务聚合仓储。</param>
/// <param name="distributedLock">分布式锁（Redis 可用时跨实例，未启用时降级进程内锁）。</param>
/// <param name="schedulingOption">调度配置（同步锁参数、misfire 策略）。</param>
/// <param name="logger">日志器。</param>
public sealed class JobScheduleManager(
    ISchedulerFactory schedulerFactory,
    IAggregateRepository<SysJob> jobRepository,
    IDistributedLock distributedLock,
    SchedulingOption schedulingOption,
    ILogger<JobScheduleManager> logger) : IJobScheduleManager
{
    /// <summary>启动同步锁键：保证同一时刻只有一个实例执行全量差异同步。</summary>
    private const string StartupSyncLockKey = "scheduling:jobs:sync";

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // 启动同步只由持锁者执行：滚动发布 / 多副本同时启动时，未抢到锁的副本跳过（持锁副本完成全量同步）
        var lockHandle = await distributedLock.AcquireAsync(
            StartupSyncLockKey,
            TimeSpan.FromSeconds(schedulingOption.SyncLockLeaseSeconds),
            TimeSpan.FromSeconds(schedulingOption.SyncLockWaitSeconds),
            cancellationToken);
        if (lockHandle is null)
        {
            logger.LogInformation("定时任务同步：未获取到启动同步锁，跳过本次同步（由持锁实例完成），锁等待 {WaitSeconds} 秒",
                schedulingOption.SyncLockWaitSeconds);
            return;
        }

        await using (lockHandle)
        {
            await SyncAllAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    /// <remarks>入调度前 fail-fast 校验 JobType：无效类型拒绝入调度并抛出业务异常（创建 / 更新路径
    /// 由验证器前置拦截，此处兜底直接写库 / 启动装载的旁路），避免静默入调度后首次执行才暴露。</remarks>
    public async Task ScheduleAsync(SysJob job, CancellationToken cancellationToken = default)
    {
        EnsureJobTypeValid(job);
        WarnIfParametersUnknown(job);
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await EnsureScheduledAsync(scheduler, job, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid jobId, string jobGroup, CancellationToken cancellationToken = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await scheduler.DeleteJob(BuildJobKey(jobId, jobGroup), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>与 <see cref="ScheduleAsync"/> 同样入调度前 fail-fast 校验 JobType。</remarks>
    public async Task RunOnceAsync(SysJob job, CancellationToken cancellationToken = default)
    {
        EnsureJobTypeValid(job);
        WarnIfParametersUnknown(job);
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var jobKey = BuildJobKey(job.Id, job.JobGroup);
        var jobDetail = BuildJobDetail(job, jobKey);

        // 暂停任务未在调度中：先确保 JobDetail 已存储，再立即触发一次（不影响既有 Cron Trigger）
        await scheduler.AddJob(jobDetail, replace: true, cancellationToken);
        await scheduler.TriggerJob(jobKey, cancellationToken);
    }

    /// <summary>
    /// 全量差异同步：装载全部运行中任务（未变化不动，变更按差异更新），并删除库中已不存在 / 已停用的调度任务。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task SyncAllAsync(CancellationToken cancellationToken)
    {
        var jobs = await jobRepository.GetListAsync([j => j.Status == CommonStatus.Enabled], cancellationToken: cancellationToken);
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var desiredKeys = new HashSet<JobKey>();

        foreach (var job in jobs)
        {
            try
            {
                desiredKeys.Add(BuildJobKey(job.Id, job.JobGroup));
                await EnsureScheduledAsync(scheduler, job, cancellationToken);
            }
            catch (Exception exception)
            {
                // 单个任务装载失败不影响其余任务与应用启动
                logger.LogError(exception, "装载定时任务失败：{JobName}（{JobType}）", job.JobName, job.JobType);
            }
        }

        // 删除库中已停用 / 已删除但仍残留在调度器中的任务（只处理本系统管理的 sys_job_ 命名空间）
        var existingKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken);
        foreach (var existingKey in existingKeys.Where(key => key.Name.StartsWith("sys_job_", StringComparison.Ordinal)))
        {
            if (desiredKeys.Contains(existingKey))
            {
                continue;
            }

            try
            {
                await scheduler.DeleteJob(existingKey, cancellationToken);
                logger.LogInformation("定时任务同步：已删除失效调度任务 {JobKey}（{Group}）", existingKey.Name, existingKey.Group);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "定时任务同步：删除失效调度任务失败 {JobKey}（{Group}）", existingKey.Name, existingKey.Group);
            }
        }

        // P2-7 结果校验：上面单任务装载的 try/catch 会吞掉失败（仅记 LogError），若不复核，
        // 应用会带着「库中启用但调度器缺失」的任务静默启动。同步后重新读取调度器实际 JobKey 集合核对。
        var actualKeys = (await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken))
            .Where(key => key.Name.StartsWith("sys_job_", StringComparison.Ordinal))
            .ToHashSet();
        var missingKeys = desiredKeys.Where(key => !actualKeys.Contains(key)).ToArray();
        if (missingKeys.Length > 0)
        {
            logger.LogError(
                "定时任务同步结果校验失败：库内启用 {Desired} 个，调度器内本系统任务 {Actual} 个，缺失 {Missing} 个（{JobKeys}），触发一次兜底重新同步",
                desiredKeys.Count, actualKeys.Count, missingKeys.Length,
                string.Join(", ", missingKeys.Select(key => key.Name)));

            // 兜底：仅对缺失任务再执行一次差异同步（不整库重扫，保持「提交后事件驱动」架构与最小副作用）
            foreach (var job in jobs.Where(job => missingKeys.Contains(BuildJobKey(job.Id, job.JobGroup))))
            {
                try
                {
                    await EnsureScheduledAsync(scheduler, job, cancellationToken);
                    logger.LogInformation("定时任务同步兜底重新同步成功：{JobName}（{JobType}）", job.JobName, job.JobType);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "定时任务同步兜底重新同步仍失败：{JobName}（{JobType}）", job.JobName, job.JobType);
                }
            }
        }

        logger.LogInformation("定时任务同步完成：库内启用 {Desired} 个，调度器内本系统任务 {Actual} 个（校验时口径）",
            desiredKeys.Count, actualKeys.Count);
    }

    /// <summary>
    /// 单个任务差异同步：不存在则新增；JobDetail 变化则替换（保留 Trigger）；Trigger 不存在则创建，
    /// Cron 变化则 RescheduleJob；全部未变则不做任何操作（保留下次触发时间语义）。
    /// </summary>
    /// <param name="scheduler">调度器。</param>
    /// <param name="job">任务定义。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task EnsureScheduledAsync(IScheduler scheduler, SysJob job, CancellationToken cancellationToken)
    {
        WarnIfParametersUnknown(job);
        var jobKey = BuildJobKey(job.Id, job.JobGroup);
        var desiredDetail = BuildJobDetail(job, jobKey);
        var desiredTrigger = BuildTrigger(job, jobKey);
        var existingDetail = await scheduler.GetJobDetail(jobKey, cancellationToken);

        if (existingDetail is null)
        {
            await scheduler.ScheduleJob(desiredDetail, desiredTrigger, cancellationToken);
            logger.LogInformation("定时任务同步：新增任务 {JobName}（{JobKey}）", job.JobName, jobKey);
            return;
        }

        if (IsJobDetailChanged(existingDetail, desiredDetail))
        {
            // 仅替换 JobDetail（trigger 保留）：并发包装类型 / 描述变化时无需重建触发器，不重置下次触发时间
            await scheduler.AddJob(desiredDetail, replace: true, cancellationToken);
            logger.LogInformation("定时任务同步：更新任务定义 {JobName}（{JobKey}）", job.JobName, jobKey);
        }

        var triggers = await scheduler.GetTriggersOfJob(jobKey, cancellationToken);
        var existingTrigger = triggers.FirstOrDefault(trigger => trigger.Key.Equals(desiredTrigger.Key));
        if (existingTrigger is null)
        {
            // 任务已持久化（StoreDurably）：为既有 JobDetail 补充触发器
            await scheduler.ScheduleJob(desiredTrigger, cancellationToken);
            logger.LogInformation("定时任务同步：补充触发器 {JobName}（{JobKey}）", job.JobName, jobKey);
            return;
        }

        if (!string.Equals(GetCronExpression(existingTrigger), job.CronExpression, StringComparison.Ordinal))
        {
            // Cron 变更：按同一 TriggerKey 重新调度，保留触发器身份并重新计算下次触发时间
            await scheduler.RescheduleJob(existingTrigger.Key, desiredTrigger, cancellationToken);
            logger.LogInformation("定时任务同步：Cron 变更重新调度 {JobName}（{JobKey}）：{OldCron} → {NewCron}",
                job.JobName, jobKey, GetCronExpression(existingTrigger), job.CronExpression);
        }
    }

    /// <summary>构造任务 JobKey（名称固定为 sys_job_{Id}，避免任务重命名导致调度失联）。</summary>
    /// <param name="jobId">任务 Id。</param>
    /// <param name="jobGroup">任务分组。</param>
    /// <returns>JobKey。</returns>
    private static JobKey BuildJobKey(Guid jobId, string jobGroup) => new($"sys_job_{jobId}", jobGroup);

    /// <summary>
    /// 构造任务定义（包装任务类型按 Concurrent 选择，JobData 携带业务任务 Id 与作业参数）。
    /// <para>作业参数逐项写入 Quartz 原生 JobDataMap，目标 IJob 直接从 MergedJobDataMap 读取；
    /// 额外写入规范化签名键供 <see cref="IsJobDetailChanged"/> 做差异比对（参数一变即替换 JobDetail）。
    /// 不自研参数绑定 / 重试 / 超时框架：恢复与 misfire 沿用 Quartz 原生能力。</para>
    /// </summary>
    /// <param name="job">任务定义。</param>
    /// <param name="jobKey">调度键。</param>
    /// <returns>JobDetail。</returns>
    private static IJobDetail BuildJobDetail(SysJob job, JobKey jobKey)
    {
        var parameters = JobParameters.Parse(job.ParametersJson);
        var builder = JobBuilder
            .Create(job.Concurrent ? typeof(JobExecuteWrapper) : typeof(JobExecuteWrapperDisallowConcurrent))
            .WithIdentity(jobKey)
            .WithDescription(job.JobName)
            .UsingJobData(JobExecuteWrapper.JobIdKey, job.Id.ToString());

        foreach (var parameter in parameters)
        {
            builder.UsingJobData(parameter.Key, parameter.Value);
        }

        return builder
            .UsingJobData(JobParameters.SignatureKey, JobParameters.Signature(parameters))
            // 执行期包装器把失败传播为 JobExecutionException，此处登记恢复语义供调度器在实例故障后重跑
            .RequestRecovery()
            .StoreDurably()
            .Build();
    }

    /// <summary>构造 Cron 触发器（应用配置的 misfire 策略）。</summary>
    /// <param name="job">任务定义。</param>
    /// <param name="jobKey">调度键。</param>
    /// <returns>触发器。</returns>
    private ITrigger BuildTrigger(SysJob job, JobKey jobKey)
    {
        return TriggerBuilder
            .Create()
            .WithIdentity($"{jobKey.Name}_trigger", jobKey.Group)
            .ForJob(jobKey)
            .WithCronSchedule(job.CronExpression, builder => ApplyMisfirePolicy(builder))
            .Build();
    }

    /// <summary>
    /// 应用 misfire 策略：FireAndProceed 立即补触发一次后继续；RescheduleNextWithRemainingCount 的 Cron 语义
    /// 为跳过错过触发、按计划计算下一次（WithMisfireHandlingInstructionDoNothing）。
    /// </summary>
    /// <param name="builder">Cron 调度构建器。</param>
    /// <returns>应用策略后的构建器。</returns>
    private CronScheduleBuilder ApplyMisfirePolicy(CronScheduleBuilder builder)
    {
        return string.Equals(schedulingOption.MisfireInstruction, "RescheduleNextWithRemainingCount", StringComparison.OrdinalIgnoreCase)
            ? builder.WithMisfireHandlingInstructionDoNothing()
            : builder.WithMisfireHandlingInstructionFireAndProceed();
    }

    /// <summary>判断既有 JobDetail 是否需要替换（包装类型、描述、业务任务 Id、作业参数签名任一变化）。</summary>
    /// <param name="existing">既有定义。</param>
    /// <param name="desired">期望定义。</param>
    /// <returns>需要替换返回 true。</returns>
    private static bool IsJobDetailChanged(IJobDetail existing, IJobDetail desired)
    {
        if (existing.JobType != desired.JobType
            || !string.Equals(existing.Description, desired.Description, StringComparison.Ordinal))
        {
            return true;
        }

        var existingJobId = ReadJobData(existing, JobExecuteWrapper.JobIdKey);
        var desiredJobId = ReadJobData(desired, JobExecuteWrapper.JobIdKey);
        if (!string.Equals(existingJobId, desiredJobId, StringComparison.Ordinal))
        {
            return true;
        }

        // 参数签名：升级前持久化的 JobDetail 没有该键，按空串处理，避免每次启动都误判为「已变更」而反复替换
        return !string.Equals(
            ReadJobData(existing, JobParameters.SignatureKey),
            ReadJobData(desired, JobParameters.SignatureKey),
            StringComparison.Ordinal);
    }

    /// <summary>读取 JobDataMap 字符串键；键不存在时返回空串。</summary>
    /// <param name="detail">任务定义。</param>
    /// <param name="key">键名。</param>
    /// <returns>键值或空串。</returns>
    private static string ReadJobData(IJobDetail detail, string key) =>
        detail.JobDataMap.ContainsKey(key) ? detail.JobDataMap.GetString(key) ?? string.Empty : string.Empty;

    /// <summary>读取触发器 Cron 表达式（非 Cron 触发器返回 null）。</summary>
    /// <param name="trigger">触发器。</param>
    /// <returns>Cron 表达式。</returns>
    private static string? GetCronExpression(ITrigger trigger)
    {
        return trigger is ICronTrigger cronTrigger ? cronTrigger.CronExpressionString : null;
    }

    /// <summary>
    /// 校验任务类型可解析：入调度 / 立即执行时接受白名单标识与已登记历史别名（存量库升级后仍可运行）；
    /// 新写入一律由 <see cref="IsValidJobType"/> 只接受白名单标识，杜绝任意 CLR 类型名。
    /// </summary>
    /// <param name="job">任务定义。</param>
    private static void EnsureJobTypeValid(SysJob job)
    {
        if (JobTypeResolver.Resolve(job.JobType) is null)
        {
            throw new BusinessException(ErrorCodes.JobTypeInvalid,
                $"任务「{job.JobName}」类型「{job.JobType}」不在已登记白名单内（允许值：{string.Join(" / ", JobTypeResolver.AllowedKeys)}），已拒绝入调度");
        }
    }

    /// <summary>
    /// 入调度前按作业类型校验参数契约（P2-F2）：未知键 / 值非法记 Warning（不阻断，保持存量库
    /// 向前兼容，作业执行侧另有越界收敛兜底）；结构合法性已由 JobParameters 双侧兜底，此处
    /// 只补「键与取值是否为该作业所认识」的契约校验，让误配在同步期可见而非首次执行才暴露。
    /// </summary>
    /// <param name="job">任务定义。</param>
    private void WarnIfParametersUnknown(SysJob job)
    {
        IReadOnlyDictionary<string, string> parameters;
        try
        {
            parameters = JobParameters.Parse(job.ParametersJson);
        }
        catch (InvalidOperationException exception)
        {
            // 结构非法已被 Parse fail-fast 抛出（后续 BuildJobDetail 同样会抛）：此处仅提前留痕定位
            logger.LogWarning("定时任务 {JobName}（{JobId}）参数结构非法，将在入调度时被拒绝：{Reason}",
                job.JobName, job.Id, exception.Message);
            return;
        }

        var problems = JobParameterContracts.Validate(job.JobType.Trim(), parameters);
        foreach (var problem in problems)
        {
            logger.LogWarning("定时任务 {JobName}（{JobId}，{JobType}）参数契约告警：{Problem}",
                job.JobName, job.Id, job.JobType, problem);
        }
    }

    /// <inheritdoc />
    public bool IsValidCronExpression(string cronExpression) => CronExpression.IsValidExpression(cronExpression);

    /// <inheritdoc />
    public bool IsValidJobType(string jobType) => JobTypeResolver.IsAllowedInput(jobType);
}
