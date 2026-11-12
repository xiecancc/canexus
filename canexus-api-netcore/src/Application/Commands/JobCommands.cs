
using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Jobs;
using AutoMapper;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services;
using Domain.Services.Events;
using Domain.Services.Rules;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Commands;
/// <summary>创建定时任务命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="jobRepository">定时任务仓储。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
/// <param name="mapper">对象映射器。</param>
public sealed class JobCreateCommandHandler(IAggregateRepository<SysJob> jobRepository, IDomainEventPublisher domainEventPublisher, IMapper mapper) : RequestHandler<JobCreateCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(JobCreateCommand command, CancellationToken cancellationToken)
    {
        // 调度同步已移至 JobScheduleSynchronizationHandler（Outbox 提交后投递），本处理器只负责 DB 写入与事件发布
        var request = command.Request;
        var entity = mapper.Map<SysJob>(request);
        // 主键与审计字段已就地填充到传入实体，供创建事件携带创建后的任务状态
        _ = await jobRepository.InsertAsync(entity, cancellationToken);

        // 关键节点发布定时任务创建领域事件（携带创建后的任务状态）
        await domainEventPublisher.PublishAsync(new JobCreatedEvent(entity), cancellationToken);
        return true;
    }
}

/// <summary>删除定时任务命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="jobRepository">定时任务仓储。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class JobDeleteCommandHandler(IAggregateRepository<SysJob> jobRepository, IDomainEventPublisher domainEventPublisher) : RequestHandler<JobDeleteCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(JobDeleteCommand command, CancellationToken cancellationToken)
    {
        // 调度同步已移至 JobScheduleSynchronizationHandler（Outbox 提交后投递），本处理器只负责 DB 写入与事件发布
        var entity = await jobRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.JobNotFound, $"定时任务 {command.Id} 不存在");

        // 变更前捕获任务状态快照（软删除可能回填墓碑字段）
        var snapshot = entity.Snapshot<SysJob>();

        await jobRepository.SoftDeleteAsync(command.Id, cancellationToken, command.Version);

        // 关键节点发布定时任务删除领域事件（携带删除前的任务状态）
        await domainEventPublisher.PublishAsync(new JobDeletedEvent(snapshot), cancellationToken);
        return true;
    }
}

/// <summary>恢复定时任务命令处理器（解除软删除墓碑并发布恢复事件；幂等执行）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="jobRepository">定时任务仓储。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class JobRestoreCommandHandler(IAggregateRepository<SysJob> jobRepository, IDomainEventPublisher domainEventPublisher) : RequestHandler<JobRestoreCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(JobRestoreCommand command, CancellationToken cancellationToken)
    {
        // 调度同步已移至 JobScheduleSynchronizationHandler（Outbox 提交后投递），本处理器只负责 DB 写入与事件发布
        // 幂等恢复：主键不存在或未删除时仓储静默忽略（返回 false），不同步调度、不发布事件
        if (!await jobRepository.RestoreAsync(command.Id, cancellationToken, command.Version))
        {
            return false;
        }

        // 恢复刚成功，聚合必然可常规查询（墓碑已解除）；取回恢复后状态供恢复事件携带
        var restored = await jobRepository.GetAsync([it => it.Id == command.Id], cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"定时任务 {command.Id} 恢复成功但不可查询，疑似数据访问层软删除过滤异常");

        // 关键节点发布定时任务恢复领域事件（携带恢复后的任务状态）
        await domainEventPublisher.PublishAsync(new JobRestoredEvent(restored), cancellationToken);
        return true;
    }
}

/// <summary>立即触发一次执行命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="jobRepository">定时任务仓储。</param>
/// <param name="scheduleManager">任务调度管理端口（触发执行与任务类型校验）。</param>
public sealed class JobRunOnceCommandHandler(IAggregateRepository<SysJob> jobRepository, IJobScheduleManager scheduleManager) : RequestHandler<JobRunOnceCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(JobRunOnceCommand command, CancellationToken cancellationToken)
    {
        var entity = await jobRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.JobNotFound, $"定时任务 {command.Id} 不存在");

        // 触发执行不改变任务聚合数据，不发布领域事件
        await scheduleManager.RunOnceAsync(entity, cancellationToken);
    }
}

/// <summary>更新定时任务命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="jobRepository">定时任务仓储。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
/// <param name="mapper">对象映射器。</param>
public sealed class JobUpdateCommandHandler(IAggregateRepository<SysJob> jobRepository, IDomainEventPublisher domainEventPublisher, IMapper mapper) : RequestHandler<JobUpdateCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(JobUpdateCommand command, CancellationToken cancellationToken)
    {
        // 调度同步已移至 JobScheduleSynchronizationHandler（Outbox 提交后投递），本处理器只负责 DB 写入与事件发布
        var request = command.Request;
        var entity = await jobRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.JobNotFound, $"定时任务 {command.Id} 不存在");

        // 变更前捕获任务状态快照（实体即将被原位修改，供修改事件携带变更前后状态）
        var before = entity.Snapshot<SysJob>();

        mapper.Map(request, entity);
        entity.ApplyVersion(command.Version);
        await jobRepository.UpdateAsync(entity, cancellationToken);

        // 关键节点发布定时任务修改领域事件（携带变更前后任务状态，启停与分组变化由监听器比对同步调度）
        await domainEventPublisher.PublishAsync(new JobUpdatedEvent(before, entity), cancellationToken);
        return true;
    }
}

/// <summary>修改定时任务状态命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="jobRepository">定时任务仓储。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class JobUpdateStatusCommandHandler(IAggregateRepository<SysJob> jobRepository, IDomainEventPublisher domainEventPublisher) : RequestHandler<JobUpdateStatusCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(JobUpdateStatusCommand command, CancellationToken cancellationToken)
    {
        // 调度同步已移至 JobScheduleSynchronizationHandler（Outbox 提交后投递），本处理器只负责 DB 写入与事件发布
        var entity = await jobRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.JobNotFound, $"定时任务 {command.Id} 不存在");

        // 变更前捕获任务状态快照（状态字段即将被原位修改）
        var before = entity.Snapshot<SysJob>();

        entity.Status = command.Request.Status;
        entity.ApplyVersion(command.Version);
        await jobRepository.UpdateAsync(entity, cancellationToken);

        // 关键节点发布定时任务修改领域事件（携带变更前后任务状态，启停变化可由监听器比对）
        await domainEventPublisher.PublishAsync(new JobUpdatedEvent(before, entity), cancellationToken);
        return true;
    }
}
