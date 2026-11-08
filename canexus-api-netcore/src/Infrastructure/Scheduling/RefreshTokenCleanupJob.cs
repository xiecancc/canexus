using Domain.Entities;
using Domain.Shared.Jobs;
using Microsoft.Extensions.Logging;
using Quartz;
using SqlSugar;

namespace Infrastructure.Scheduling;

/// <summary>
/// 过期刷新令牌回收作业（T5 #4 第二个示例业务作业）：按 <c>GraceDays</c> 参数回收早已过期的令牌行。
/// <para>与 <see cref="LogRetentionJob"/> 共用 Quartz 原生 <c>JobDataMap</c> 参数通道，展示
/// 「稳定短标识 → DI 可解析的 IJob 实现」注册表如何承载真实业务作业，而不是只能跑 noop。</para>
/// <para><b>刻意不调用 <c>ISysRefreshTokenRepository.RemoveExpiredAsync</c></b>：该方法会把
/// 「已吊销但尚未过期」的记录一并删除，而这类记录正是 T4 令牌家族重用检测的证据
/// （<c>GetRevokedByTokenAsync</c>）。本作业只回收「过期时间已早于宽限期截止点」的行，
/// 保留宽限期内的吊销记录，不缩短重用检测窗口。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="client">SqlSugar 客户端（SqlSugarScope，作用域内独立上下文）。</param>
/// <param name="logger">日志器。</param>
public sealed class RefreshTokenCleanupJob(ISqlSugarClient client, ILogger<RefreshTokenCleanupJob> logger) : IJob
{
    /// <summary>已登记的稳定白名单标识（写入 SysJob.JobType）。</summary>
    public const string JobKey = JobTypeResolver.RefreshTokenCleanupJobKey;

    /// <summary>宽限天数参数键。</summary>
    public const string GraceDaysParameter = "GraceDays";

    /// <summary>默认宽限天数（过期后保留 7 天再回收）。</summary>
    public const int DefaultGraceDays = 7;

    /// <summary>允许的最小宽限天数。</summary>
    public const int MinGraceDays = 0;

    /// <summary>允许的最大宽限天数。</summary>
    public const int MaxGraceDays = 365;

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

        var graceDays = JobParameters.GetInt(
            rawParameters, GraceDaysParameter, DefaultGraceDays, MinGraceDays, MaxGraceDays);
        var removed = await CleanAsync(graceDays, context.CancellationToken);

        logger.LogInformation("过期刷新令牌回收完成：GraceDays={GraceDays}，删除行数={Removed}", graceDays, removed);
    }

    /// <summary>回收过期时间早于宽限期截止点的刷新令牌行（可直接被回归测试调用）。</summary>
    /// <param name="graceDays">宽限天数；越界时收敛到 <see cref="MinGraceDays"/>~<see cref="MaxGraceDays"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除的行数。</returns>
    public Task<int> CleanAsync(int graceDays, CancellationToken cancellationToken = default)
    {
        var days = Math.Clamp(graceDays, MinGraceDays, MaxGraceDays);
        var cutoff = DateTime.UtcNow.AddDays(-days);
        cancellationToken.ThrowIfCancellationRequested();

        // QueryFilter 不作用于 Deleteable（V-04）；令牌回收是平台级行为，不过滤租户
        return client.Deleteable<SysRefreshToken>()
            .Where(token => token.ExpiresAt < cutoff)
            .ExecuteCommandAsync(cancellationToken);
    }
}
