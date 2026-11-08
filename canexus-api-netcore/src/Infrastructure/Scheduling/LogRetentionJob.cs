using Domain.Entities;
using Domain.Shared.Jobs;
using Microsoft.Extensions.Logging;
using Quartz;
using SqlSugar;

namespace Infrastructure.Scheduling;

/// <summary>
/// 日志保留作业（MNT-04 / T5 #5）：按 <c>RetentionDays</c> 参数清理四类日志表的过期行。
/// <para>复用现有 Quartz 调度器与 #4 建立的 <c>JobDataMap</c> 参数通道，不新增 BackgroundService、
/// 不自研定时框架；参数经 <see cref="JobParameters"/> 统一解析，缺省 30 天，越界回退到缺省值并记日志。</para>
/// <para>平台级保留策略对全部租户生效：SqlSugar 的 <c>QueryFilter</c> 不作用于
/// <c>Deleteable</c>（T2 / V-04 实测结论），故此处删除不会被当前租户上下文收窄。
/// 执行本身由 <see cref="JobExecuteWrapper"/> 写入 sys_job_log 留痕，并按结构化日志记录各表删除行数。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端（SqlSugarScope，作用域内独立上下文）。</param>
/// <param name="logger">日志器。</param>
public sealed class LogRetentionJob(ISqlSugarClient client, ILogger<LogRetentionJob> logger) : IJob
{
    /// <summary>已登记的稳定白名单标识（写入 SysJob.JobType）。</summary>
    public const string JobKey = JobTypeResolver.LogRetentionJobKey;

    /// <summary>保留天数参数键。</summary>
    public const string RetentionDaysParameter = "RetentionDays";

    /// <summary>默认保留天数（未配置或参数非法时回退）。</summary>
    public const int DefaultRetentionDays = 30;

    /// <summary>允许的最小保留天数（防止误配为 0 清空全部日志）。</summary>
    public const int MinRetentionDays = 1;

    /// <summary>允许的最大保留天数。</summary>
    public const int MaxRetentionDays = 3650;

    /// <summary>执行清理：从 Quartz 上下文读取参数并调用 <see cref="CleanAsync"/>。</summary>
    /// <param name="context">Quartz 执行上下文。</param>
    /// <returns>表示异步操作的任务。</returns>
    public async Task Execute(IJobExecutionContext context)
    {
        var rawParameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in context.MergedJobDataMap.Keys)
        {
            if (!string.Equals(key, JobParameters.SignatureKey, StringComparison.Ordinal)
                && !string.Equals(key, JobParameters.JobIdKey, StringComparison.Ordinal))
            {
                rawParameters[key] = context.MergedJobDataMap.GetString(key) ?? string.Empty;
            }
        }

        var retentionDays = JobParameters.GetInt(
            rawParameters, RetentionDaysParameter, DefaultRetentionDays, MinRetentionDays, MaxRetentionDays);
        var summary = await CleanAsync(retentionDays, context.CancellationToken);

        logger.LogInformation(
            "日志保留清理完成：RetentionDays={RetentionDays}，删除行数 LoginLog={LoginLog} / OperLog={OperLog} / EntityChangeLog={EntityChangeLog} / JobLog={JobLog}",
            retentionDays,
            summary[nameof(SysLoginLog)],
            summary[nameof(SysOperLog)],
            summary[nameof(SysEntityChangeLog)],
            summary[nameof(SysJobLog)]);
    }

    /// <summary>
    /// 按统一时间基准清理四类日志表的过期行（可直接被回归测试调用，无需构造 Quartz 上下文）。
    /// </summary>
    /// <param name="retentionDays">保留天数；越界时收敛到 <see cref="MinRetentionDays"/>~<see cref="MaxRetentionDays"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>各表删除行数（键为实体类型名）。</returns>
    public async Task<IReadOnlyDictionary<string, int>> CleanAsync(
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        var days = Math.Clamp(retentionDays, MinRetentionDays, MaxRetentionDays);
        // 单一时间基准：四表同步裁剪，避免逐表计算产生跨秒边界差异
        var cutoff = DateTime.UtcNow.AddDays(-days);
        cancellationToken.ThrowIfCancellationRequested();

        // 日志表不走软删除墓碑，与既有 ClearAsync 口径一致（物理删除）
        var loginLog = await client.Deleteable<SysLoginLog>()
            .Where(log => log.CreatedAt < cutoff)
            .ExecuteCommandAsync(cancellationToken);
        var operLog = await client.Deleteable<SysOperLog>()
            .Where(log => log.CreatedAt < cutoff)
            .ExecuteCommandAsync(cancellationToken);
        var entityChangeLog = await client.Deleteable<SysEntityChangeLog>()
            .Where(log => log.CreatedAt < cutoff)
            .ExecuteCommandAsync(cancellationToken);
        var jobLog = await client.Deleteable<SysJobLog>()
            .Where(log => log.CreatedAt < cutoff)
            .ExecuteCommandAsync(cancellationToken);

        return new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [nameof(SysLoginLog)] = loginLog,
            [nameof(SysOperLog)] = operLog,
            [nameof(SysEntityChangeLog)] = entityChangeLog,
            [nameof(SysJobLog)] = jobLog
        };
    }
}
