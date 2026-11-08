using Application.Events;
using Domain.Repositories;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace API.BackgroundServices;

/// <summary>
/// Outbox 后台投递 worker（P1-A2 / P1-B）：按配置间隔批量认领待处理 Outbox 消息并投递。
/// <para>兜底场景：进程在「事务提交后、同步投递完成前」崩溃，或同步投递失败——事件已持久化，
/// 由本 worker 从 Outbox 表恢复投递；单条消息失败记录次数与原因并按退避等待重试，超过上限置为失败（死信）。</para>
/// <para>多副本并发：认领使用条件 UPDATE（Pending → Processing + ClaimOwner + 租约），同一消息同一时刻
/// 只有一个副本投递；租约过期后其他副本可重新认领，避免副本崩溃导致消息卡死。
/// 本实例标识在启动时生成，用于区分认领者。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="scopeFactory">服务作用域工厂（每次轮询独立作用域，隔离仓储连接）。</param>
/// <param name="options">Outbox 配置。</param>
/// <param name="logger">日志器。</param>
public sealed class OutboxDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOption> options,
    ILogger<OutboxDeliveryWorker> logger) : BackgroundService
{
    /// <summary>清理周期（终态消息清理与超期 Pending 判定至多每小时执行一轮，与轮询解耦）。</summary>
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    /// <summary>上轮清理时间（worker 单例实例字段；null 表示尚未清理过，启动后首轮即执行）。</summary>
    private DateTimeOffset? _lastPurgeAt;

    /// <summary>本副本实例标识（主机名 + 进程 Id + 启动随机串），作为 Outbox 认领者标识。</summary>
    private readonly string _instanceId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Outbox 投递 worker 未启用（Outbox:Enabled=false），跳过后台轮询");
            return;
        }

        logger.LogInformation("Outbox 投递 worker 已启动（实例 {InstanceId}）：轮询间隔 {Interval} 秒，单批 {BatchSize} 条，最大重试 {MaxAttempts} 次，租约 {LeaseSeconds} 秒，已投递保留 {RetentionDays} 天 / 死信保留 {FailedRetentionDays} 天 / 超期判定 {StalePendingDays} 天",
            _instanceId, options.Value.PollIntervalSeconds, options.Value.BatchSize, options.Value.MaxAttempts, options.Value.LeaseSeconds,
            options.Value.RetentionDays, options.Value.FailedRetentionDays, options.Value.StalePendingDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeliverPendingBatchAsync(stoppingToken);

                // 终态清理 + 超期 Pending 判定：三个保留 / 判定开关均为 0 时跳过；清理失败仅告警不影响投递
                var purgeEnabled = options.Value.RetentionDays > 0
                    || options.Value.FailedRetentionDays > 0
                    || options.Value.StalePendingDays > 0;
                if (purgeEnabled
                    && (_lastPurgeAt is null || DateTimeOffset.UtcNow - _lastPurgeAt >= PurgeInterval))
                {
                    await PurgeExpiredAsync(stoppingToken);
                    _lastPurgeAt = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox 投递轮询异常（下轮继续）");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), stoppingToken);
        }

        logger.LogInformation("Outbox 投递 worker 已停止");
    }

    /// <summary>认领一批待处理消息并投递（独立作用域；单条失败由投递服务记录并按退避等待重试）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task DeliverPendingBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxMessageRepository>();
        var deliveryService = scope.ServiceProvider.GetRequiredService<IOutboxDeliveryService>();

        var batch = await repository.ClaimBatchAsync(
            options.Value.BatchSize,
            _instanceId,
            TimeSpan.FromSeconds(options.Value.LeaseSeconds),
            cancellationToken);
        if (batch.Count > 0)
        {
            await deliveryService.DeliverAsync(batch, _instanceId, cancellationToken);
        }
    }

    /// <summary>
    /// 清理终态消息并收敛超期未完成投递的消息（独立作用域；分批执行，单轮至多一批，剩余由下轮收敛）。
    /// 超期 Pending / Processing 先转死信记录原因，再按死信保留期删除，避免 Outbox 表无限增长。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxMessageRepository>();
            var now = DateTime.UtcNow;
            var maxRows = options.Value.BatchSize;

            if (options.Value.StalePendingDays > 0)
            {
                var stale = await repository.MarkStaleAsFailedAsync(
                    now.AddDays(-options.Value.StalePendingDays),
                    $"超过 Outbox:StalePendingDays（{options.Value.StalePendingDays} 天）仍未完成投递，转为死信",
                    maxRows,
                    cancellationToken);
                if (stale > 0)
                {
                    logger.LogWarning("Outbox 已将 {Stale} 条超过 {StaleDays} 天未完成投递的消息转为死信",
                        stale, options.Value.StalePendingDays);
                }
            }

            // 0 = 对应终态不清理（传 null），避免构造越界的 DateTime.MinValue SQL 参数
            DateTime? processedCutoff = options.Value.RetentionDays > 0
                ? now.AddDays(-options.Value.RetentionDays)
                : null;
            DateTime? failedCutoff = options.Value.FailedRetentionDays > 0
                ? now.AddDays(-options.Value.FailedRetentionDays)
                : null;
            var deleted = await repository.DeleteCompletedBeforeAsync(processedCutoff, failedCutoff, maxRows, cancellationToken);
            if (deleted > 0)
            {
                logger.LogInformation("Outbox 已清理超过保留期的终态消息 {Deleted} 条（已投递 {RetentionDays} 天 / 死信 {FailedRetentionDays} 天）",
                    deleted, options.Value.RetentionDays, options.Value.FailedRetentionDays);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 清理为运维性任务：失败仅告警，不影响投递主流程
            logger.LogWarning(exception, "Outbox 终态消息清理失败（下个清理周期重试）");
        }
    }
}
