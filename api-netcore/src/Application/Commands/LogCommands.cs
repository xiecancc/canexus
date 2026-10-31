namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Enums;

/// <summary>清空全部登录日志命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="loginLogRepository">登录日志仓储（物理清空）。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class CleanLoginLogCommandHandler(ILoginLogRepository loginLogRepository, IDomainEventPublisher domainEventPublisher) : CommandHandler<CleanLoginLogCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(CleanLoginLogCommand command, CancellationToken cancellationToken)
    {
        await loginLogRepository.ClearAsync(cancellationToken);

        // 关键节点发布登录日志清空领域事件（聚合根标识为 null：无单条定位）
        await domainEventPublisher.PublishAsync(new LogChangedEvent(LogChangedEvent.LoginLogType, AggregateChangeType.Deleted), cancellationToken);
    }
}

/// <summary>清空全部操作日志命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="operLogRepository">操作日志仓储（物理清空）。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class CleanOperLogCommandHandler(IOperLogRepository operLogRepository, IDomainEventPublisher domainEventPublisher) : CommandHandler<CleanOperLogCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(CleanOperLogCommand command, CancellationToken cancellationToken)
    {
        await operLogRepository.ClearAsync(cancellationToken);

        // 关键节点发布操作日志清空领域事件（聚合根标识为 null：无单条定位）
        await domainEventPublisher.PublishAsync(new LogChangedEvent(LogChangedEvent.OperLogType, AggregateChangeType.Deleted), cancellationToken);
    }
}

/// <summary>删除单条登录日志命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="loginLogRepository">登录日志仓储（物理删除）。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class DeleteLoginLogCommandHandler(ILoginLogRepository loginLogRepository, IDomainEventPublisher domainEventPublisher) : CommandHandler<DeleteLoginLogCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteLoginLogCommand command, CancellationToken cancellationToken)
    {
        // 日志不走软删除墓碑：与清空策略一致，按 Id 物理删除
        await loginLogRepository.HardDeleteAsync(command.Id, cancellationToken);

        // 关键节点发布登录日志删除领域事件（响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new LogChangedEvent(LogChangedEvent.LoginLogType, AggregateChangeType.Deleted, command.Id), cancellationToken);
    }
}

/// <summary>删除单条操作日志命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="operLogRepository">操作日志仓储（物理删除）。</param>
/// <param name="domainEventPublisher">领域事件发布端口。</param>
public sealed class DeleteOperLogCommandHandler(IOperLogRepository operLogRepository, IDomainEventPublisher domainEventPublisher) : CommandHandler<DeleteOperLogCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteOperLogCommand command, CancellationToken cancellationToken)
    {
        // 日志不走软删除墓碑：与清空策略一致，按 Id 物理删除
        await operLogRepository.HardDeleteAsync(command.Id, cancellationToken);

        // 关键节点发布操作日志删除领域事件（响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new LogChangedEvent(LogChangedEvent.OperLogType, AggregateChangeType.Deleted, command.Id), cancellationToken);
    }
}
