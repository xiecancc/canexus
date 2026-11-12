
using System.Diagnostics;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Jobs;
using Domain.Shared.Results;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Infrastructure.Scheduling;
/// <summary>
/// 调度包装任务：按 JobDataMap 中的 jobId 从库中加载任务定义，经 JobTypeResolver 白名单注册表
/// 取得目标 IJob 类型并执行（不反射任意 CLR 类型）；作业参数由 Quartz 原生 MergedJobDataMap 透传给目标作业；
/// 执行前后统一写执行日志（SysJobLog），异常信息截断 2000 字符。
/// </summary>
/// <remarks>构造函数（由 Quartz 的 Microsoft DI 作业工厂按执行作用域创建）。</remarks>
public class JobExecuteWrapper(
    IAggregateRepository<SysJob> jobRepository,
    IAggregateRepository<SysJobLog> jobLogRepository,
    IServiceProvider serviceProvider,
    ILogger<JobExecuteWrapper> logger) : IJob
{
    /// <summary>异常信息列宽上限。</summary>
    private const int ExceptionMaxLength = 2000;

    /// <summary>任务主键在 JobDataMap 中的键名（与 <see cref="JobParameters"/> 的保留键口径统一）。</summary>
    public const string JobIdKey = JobParameters.JobIdKey;

    /// <summary>执行包装任务：解析目标 IJob、计时执行并记录执行日志。</summary>
    /// <param name="context">执行上下文。</param>
    public async Task Execute(IJobExecutionContext context)
    {
        var jobId = context.MergedJobDataMap.ContainsKey(JobIdKey)
            ? Guid.TryParse(context.MergedJobDataMap.GetString(JobIdKey), out var parsedJobId) ? parsedJobId : Guid.Empty
            : Guid.Empty;
        var job = jobId != Guid.Empty ? await jobRepository.GetAsync([it => it.Id == jobId]) : null;
        if (job is null)
        {
            logger.LogWarning("定时任务 {JobId} 不存在或已删除，跳过执行", jobId);
            return;
        }

        var log = new SysJobLog
        {
            JobId = job.Id,
            JobName = job.JobName,
            Status = ExecutionStatus.Success,
            Result = "执行成功",
            TriggerTime = DateTime.UtcNow
        };
        var stopwatch = Stopwatch.StartNew();
        Exception? failure = null;
        try
        {
            var targetType = JobTypeResolver.Resolve(job.JobType)
                ?? throw new BusinessException(ErrorCodes.JobTypeInvalid, $"任务类型「{job.JobType}」不在已登记白名单内");

            var instance = (IJob)(serviceProvider.GetService(targetType) ?? Activator.CreateInstance(targetType)!);
            await instance.Execute(context);
        }
        catch (Exception exception)
        {
            failure = exception;
            log.Status = ExecutionStatus.Failure;
            log.Result = "执行失败";
            log.Exception = TruncateException(exception.ToString());
            // P2-7 任务级失败告警：结构化字段含 JobKey / 异常类型 / 耗时，便于按任务聚合与告警；
            // SysJobLog 留痕仍在 finally 中统一写库，这里不重复写库、不改表结构
            logger.LogError(exception,
                "定时任务执行失败：JobKey={JobKey} JobName={JobName} JobId={JobId} ExceptionType={ExceptionType} CostMs={CostMs}",
                context.JobDetail.Key, job.JobName, job.Id, exception.GetType().FullName, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            stopwatch.Stop();
            log.CostMs = stopwatch.ElapsedMilliseconds;
            try
            {
                await jobLogRepository.InsertAsync(log);
            }
            catch (Exception logException)
            {
                // 日志写入失败不影响调度主流程：仅记录告警
                logger.LogWarning(logException, "写入定时任务执行日志失败：{JobName}", job.JobName);
            }
        }

        // 失败必须传播给 Quartz：否则包装器吞掉异常会被判定为执行成功，
        // 既不触发 JobDetail.RequestRecovery 的恢复路径，也不产生调度器级失败记录
        if (failure is not null)
        {
            throw new JobExecutionException($"定时任务「{job.JobName}」执行失败", failure, refireImmediately: false);
        }
    }

    /// <summary>异常信息截断（列宽 2000 字符）。</summary>
    private static string TruncateException(string text) => text.Length <= ExceptionMaxLength ? text : text[..ExceptionMaxLength];
}

/// <summary>禁止并发执行的包装任务：Concurrent=false 的任务以该类型入调度，重叠触发排队执行而非并行。</summary>
/// <remarks>构造函数（透传依赖到基类）。</remarks>
[DisallowConcurrentExecution]
public sealed class JobExecuteWrapperDisallowConcurrent(
    IAggregateRepository<SysJob> jobRepository,
    IAggregateRepository<SysJobLog> jobLogRepository,
    IServiceProvider serviceProvider,
    ILogger<JobExecuteWrapper> logger) : JobExecuteWrapper(jobRepository, jobLogRepository, serviceProvider, logger)
{
}
