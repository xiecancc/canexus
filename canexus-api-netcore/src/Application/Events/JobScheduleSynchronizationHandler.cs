using Domain.Entities;
using Domain.Events;
using Domain.Services;
using Domain.Shared.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Events;

/// <summary>
/// 定时任务调度同步监听器：订阅 Outbox 提交后投递的领域事件通知，把定时任务聚合的创建 / 修改 / 删除 / 恢复
/// 变更同步到 Quartz 调度器。命令处理器只负责 DB 写入与事件发布，调度同步全部收口到本监听器，
/// 保证「先提交、后同步」——监听器不会读到未提交的中间态。
/// <para>同步口径（逐行对齐改造前各命令处理器）：创建 / 恢复仅对运行中（<c>CommonStatus.Enabled</c>）
/// 任务入调度；删除移出调度；修改按启用入调度、停用移出调度，并在分组变更时额外清理旧 JobKey。
/// 立即执行（RunOnceAsync）不发布领域事件，故无对应事件需处理。</para>
/// <para>补偿语义：本监听器抛出的异常由 <see cref="IsolatedNotificationPublisher"/> 聚合后向上传播，
/// <see cref="OutboxDeliveryService"/> 据此不把消息标记为已处理，后台 worker 按 Outbox 重试策略重投，
/// 因此 Quartz 调用失败天然获得重试、不会丢失调度变更。相应地，<c>ScheduleAsync</c> / <c>RemoveAsync</c>
/// 必须幂等：实现上 ScheduleAsync 采用差异比对（定义未变不动、变化才替换 / 重调度），
/// RemoveAsync 走 Quartz DeleteJob（JobKey 不存在时返回 false 而不抛异常），二者均可安全重复执行。
/// 需注意入调度前的 JobType 白名单校验属永久失败，若发生会耗尽重试次数进入死信，需人工介入。</para>
/// <para>注册约定：作为 INotificationHandler 实现，由 MediatR 程序集扫描自动注册，无需改动任何 DI 文件。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="scheduleManager">任务调度管理端口（Quartz 实现）。</param>
/// <param name="logger">日志器。</param>
public sealed class JobScheduleSynchronizationHandler(
    IJobScheduleManager scheduleManager,
    ILogger<JobScheduleSynchronizationHandler> logger) : INotificationHandler<DomainEventNotification>
{
    /// <inheritdoc />
    public Task Handle(DomainEventNotification notification, CancellationToken cancellationToken)
    {
        return notification.DomainEvent switch
        {
            // 创建 / 恢复：仅运行中任务入调度，暂停任务只入库（对齐 JobCreate / JobRestore 命令处理器原口径）
            JobCreatedEvent created => SyncCreatedOrRestoredAsync(created.NewEntity, cancellationToken),
            JobRestoredEvent restored => SyncCreatedOrRestoredAsync(restored.NewEntity, cancellationToken),

            // 删除：移出调度（事件携带删除前状态，JobGroup 未变）
            JobDeletedEvent deleted => RemoveJobAsync(deleted.NewEntity.Id, deleted.NewEntity.JobGroup, cancellationToken),

            // 修改（含启停 / 分组变更）：复现 JobUpdate / JobUpdateStatus 命令处理器的原同步口径
            JobUpdatedEvent updated => SyncUpdatedAsync(updated, cancellationToken),

            // 其它事件（含立即执行相关事件）不参与调度同步
            _ => Task.CompletedTask,
        };
    }

    /// <summary>创建 / 恢复同步：仅运行中任务入调度，暂停任务只入库不做调度变更。</summary>
    /// <param name="job">创建后 / 恢复后的任务状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task SyncCreatedOrRestoredAsync(SysJob job, CancellationToken cancellationToken)
    {
        if (job.Status == CommonStatus.Enabled)
        {
            await ScheduleJobAsync(job, cancellationToken);
        }
    }

    /// <summary>把任务加入调度并记录同步日志。</summary>
    /// <param name="job">任务定义。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private Task ScheduleJobAsync(SysJob job, CancellationToken cancellationToken)
    {
        logger.LogDebug("定时任务调度同步：入调度 {JobId}（{JobGroup}）", job.Id, job.JobGroup);
        return scheduleManager.ScheduleAsync(job, cancellationToken);
    }

    /// <summary>把任务移出调度并记录同步日志。</summary>
    /// <param name="jobId">任务 Id。</param>
    /// <param name="jobGroup">任务分组（JobKey 的一部分）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private Task RemoveJobAsync(Guid jobId, string jobGroup, CancellationToken cancellationToken)
    {
        logger.LogDebug("定时任务调度同步：移出调度 {JobId}（{JobGroup}）", jobId, jobGroup);
        return scheduleManager.RemoveAsync(jobId, jobGroup, cancellationToken);
    }

    /// <summary>按修改事件同步调度：分组变更先清理旧 JobKey，再按新状态入 / 出调度。</summary>
    /// <param name="updated">修改领域事件（携带变更前快照与变更后状态）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task SyncUpdatedAsync(JobUpdatedEvent updated, CancellationToken cancellationToken)
    {
        var before = updated.OldEntity;
        var after = updated.NewEntity;

        // 分组变更时旧 JobKey 失效，需一并清理旧调度（对齐 JobUpdateCommandHandler 原逻辑）
        if (!string.Equals(before.JobGroup, after.JobGroup, StringComparison.Ordinal))
        {
            await RemoveJobAsync(after.Id, before.JobGroup, cancellationToken);
        }

        // 启用则入调度、停用则移出；状态命令与更新命令口径统一（对齐两处理器原逻辑）
        if (after.Status == CommonStatus.Enabled)
        {
            await ScheduleJobAsync(after, cancellationToken);
        }
        else
        {
            await RemoveJobAsync(after.Id, after.JobGroup, cancellationToken);
        }
    }
}
