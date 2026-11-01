
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
/// <param name="scheduleManager">任务调度管理端口（Cron / 任务类型校验与调度同步）。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class JobCreateCommandHandler(IAggregateRepository<SysJob> jobRepository, IJobScheduleManager scheduleManager, IDomainEventPublisher domainEventPublisher, IMapper mapper) : RequestHandler<JobCreateCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(JobCreateCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = mapper.Map<SysJob>(request);
        _ = await jobRepository.InsertAsync(entity, cancellationToken);

        // 运行中任务创建后立即入调度；暂停任务仅入库（主键与审计字段已就地填充到传入实体）
        if (request.Status == CommonStatus.Enabled)
        {
            await scheduleManager.ScheduleAsync(entity, cancellationToken);
        }

        // 关键节点发布定时任务创建领域事件（携带创建后的任务状态）
        await domainEventPublisher.PublishAsync(new JobCreatedEvent(entity), cancellationToken);
        return true;
    }
}

/// <summary>删除定时任务命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="jobRepository">定时任务仓储。</param>
/// <param name="scheduleManager">任务调度管理端口（删除后移出调度）。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class JobDeleteCommandHandler(IAggregateRepository<SysJob> jobRepository, IJobScheduleManager scheduleManager, IDomainEventPublisher domainEventPublisher) : RequestHandler<JobDeleteCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(JobDeleteCommand command, CancellationToken cancellationToken)
    {
        var entity = await jobRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.JobNotFound, $"定时任务 {command.Id} 不存在");

        // 变更前捕获任务状态快照（软删除可能回填墓碑字段）
        var snapshot = entity.Snapshot<SysJob>();

        await scheduleManager.RemoveAsync(entity.Id, entity.JobGroup, cancellationToken);
        await jobRepository.DeleteAsync(command.Id, cancellationToken);

        // 关键节点发布定时任务删除领域事件（携带删除前的任务状态）
        await domainEventPublisher.PublishAsync(new JobDeletedEvent(snapshot), cancellationToken);
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
/// <param name="scheduleManager">任务调度管理端口（Cron / 任务类型校验与调度同步）。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class JobUpdateCommandHandler(IAggregateRepository<SysJob> jobRepository, IJobScheduleManager scheduleManager, IDomainEventPublisher domainEventPublisher, IMapper mapper) : RequestHandler<JobUpdateCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(JobUpdateCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = await jobRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.JobNotFound, $"定时任务 {command.Id} 不存在");

        var jobName = request.JobName.Trim();
        var jobGroup = request.JobGroup.Trim();

        var oldGroup = entity.JobGroup;

        // 变更前捕获任务状态快照（实体即将被原位修改）
        var before = entity.Snapshot<SysJob>();

        mapper.Map(request, entity);
        await jobRepository.UpdateAsync(entity, cancellationToken);

        // 分组变更时旧 JobKey 失效，需一并清理旧调度
        if (!string.Equals(oldGroup, jobGroup, StringComparison.Ordinal))
        {
            await scheduleManager.RemoveAsync(command.Id, oldGroup, cancellationToken);
        }

        if (request.Status == CommonStatus.Enabled)
        {
            await scheduleManager.ScheduleAsync(entity, cancellationToken);
        }
        else
        {
            await scheduleManager.RemoveAsync(command.Id, jobGroup, cancellationToken);
        }

        // 关键节点发布定时任务修改领域事件（携带变更前后任务状态）
        await domainEventPublisher.PublishAsync(new JobUpdatedEvent(before, entity), cancellationToken);
        return true;
    }
}

/// <summary>修改定时任务状态命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="jobRepository">定时任务仓储。</param>
/// <param name="scheduleManager">任务调度管理端口（启停同步调度）。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class JobUpdateStatusCommandHandler(IAggregateRepository<SysJob> jobRepository, IJobScheduleManager scheduleManager, IDomainEventPublisher domainEventPublisher) : RequestHandler<JobUpdateStatusCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(JobUpdateStatusCommand command, CancellationToken cancellationToken)
    {
        var entity = await jobRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.JobNotFound, $"定时任务 {command.Id} 不存在");

        // 变更前捕获任务状态快照（状态字段即将被原位修改）
        var before = entity.Snapshot<SysJob>();

        entity.Status = command.Request.Status;
        await jobRepository.UpdateAsync(entity, cancellationToken);

        if (command.Request.Status == CommonStatus.Enabled)
        {
            await scheduleManager.ScheduleAsync(entity, cancellationToken);
        }
        else
        {
            await scheduleManager.RemoveAsync(entity.Id, entity.JobGroup, cancellationToken);
        }

        // 关键节点发布定时任务修改领域事件（携带变更前后任务状态，启停变化可由监听器比对）
        await domainEventPublisher.PublishAsync(new JobUpdatedEvent(before, entity), cancellationToken);
        return true;
    }
}
