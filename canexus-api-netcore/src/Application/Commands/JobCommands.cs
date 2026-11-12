
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
public sealed class JobCreateCommandHandler(
    IAggregateRepository<SysJob> jobRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : AggregateCreateCommandHandler<JobCreateCommand, JobInputRequest, SysJob>(jobRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override Task ValidateAsync(JobInputRequest request, CancellationToken cancellationToken)
    {
        // 请求结构合法性由 JobCreateCommandValidator 启动期强制，此处无额外业务校验；
        // 调度同步已移至 JobScheduleSynchronizationHandler（Outbox 提交后投递），不在此处理
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override SysJob MapToEntity(JobInputRequest request)
    {
        return mapper.Map<SysJob>(request);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysJob inserted)
    {
        // 关键节点发布定时任务创建领域事件（主键与审计字段已就地填充，携带创建后的任务状态）
        return new JobCreatedEvent(inserted);
    }
}

/// <summary>删除定时任务命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="jobRepository">定时任务仓储。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class JobDeleteCommandHandler(
    IAggregateRepository<SysJob> jobRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateDeleteCommandHandler<JobDeleteCommand, SysJob>(jobRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.JobNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "定时任务";

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysJob deleted)
    {
        // 关键节点发布定时任务删除领域事件（基类快照携带删除前的任务状态；调度同步由 Outbox 监听器处理）
        return new JobDeletedEvent(deleted);
    }
}

/// <summary>恢复定时任务命令处理器（解除软删除墓碑并发布恢复事件；幂等执行）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="jobRepository">定时任务仓储。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class JobRestoreCommandHandler(
    IAggregateRepository<SysJob> jobRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateRestoreCommandHandler<JobRestoreCommand, SysJob>(jobRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysJob restored)
    {
        // 关键节点发布定时任务恢复领域事件（幂等：主键不存在或未删除时不发布，不同步调度）
        return new JobRestoredEvent(restored);
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
public sealed class JobUpdateCommandHandler(
    IAggregateRepository<SysJob> jobRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : AggregateUpdateCommandHandler<JobUpdateCommand, JobInputRequest, SysJob>(jobRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.JobNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "定时任务";

    /// <inheritdoc />
    protected override Task ValidateAsync(JobInputRequest request, SysJob entity, CancellationToken cancellationToken)
    {
        // 请求结构合法性由 JobUpdateCommandValidator 启动期强制，此处无额外业务校验
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void MapToEntity(JobInputRequest request, SysJob entity)
    {
        mapper.Map(request, entity);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysJob before, SysJob after)
    {
        // 关键节点发布定时任务修改领域事件（携带变更前后任务状态，启停与分组变化由监听器比对同步调度）
        return new JobUpdatedEvent(before, after);
    }
}

/// <summary>修改定时任务状态命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="jobRepository">定时任务仓储。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class JobUpdateStatusCommandHandler(
    IAggregateRepository<SysJob> jobRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateUpdateCommandHandler<JobUpdateStatusCommand, JobStatusRequest, SysJob>(jobRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.JobNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "定时任务";

    /// <inheritdoc />
    protected override Task ValidateAsync(JobStatusRequest request, SysJob entity, CancellationToken cancellationToken)
    {
        // 状态取值合法性由 JobUpdateStatusCommandValidator 启动期强制，此处无额外业务校验
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void MapToEntity(JobStatusRequest request, SysJob entity)
    {
        entity.Status = request.Status;
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysJob before, SysJob after)
    {
        // 关键节点发布定时任务修改领域事件（携带变更前后任务状态，启停变化由监听器比对同步调度）
        return new JobUpdatedEvent(before, after);
    }
}
