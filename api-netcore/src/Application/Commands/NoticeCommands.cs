namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Notices;
using AutoMapper;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

/// <summary>创建通知公告命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class CreateNoticeCommandHandler(IAggregateRepository<SysNotice> noticeRepository, IDomainEventPublisher domainEventPublisher, IMapper mapper) : CommandHandler<CreateNoticeCommand, Guid>
{
    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateNoticeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = mapper.Map<SysNotice>(request);
        var inserted = await noticeRepository.InsertAsync(entity, cancellationToken);

        // 关键节点发布通知公告创建领域事件（消息推送等响应动作可由监听器扩展处理）
        await domainEventPublisher.PublishAsync(new NoticeChangedEvent(inserted.Id, AggregateChangeType.Created), cancellationToken);
        return inserted.Id;
    }
}

/// <summary>删除通知公告命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DeleteNoticeCommandHandler(IAggregateRepository<SysNotice> noticeRepository, IDomainEventPublisher domainEventPublisher) : CommandHandler<DeleteNoticeCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteNoticeCommand command, CancellationToken cancellationToken)
    {
        var entity = await noticeRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NoticeNotFound, $"通知公告 {command.Id} 不存在");

        await noticeRepository.DeleteAsync(entity, cancellationToken);

        // 关键节点发布通知公告删除领域事件（响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new NoticeChangedEvent(command.Id, AggregateChangeType.Deleted), cancellationToken);
    }
}

/// <summary>更新通知公告命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UpdateNoticeCommandHandler(IAggregateRepository<SysNotice> noticeRepository, IDomainEventPublisher domainEventPublisher, IMapper mapper) : CommandHandler<UpdateNoticeCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateNoticeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = await noticeRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NoticeNotFound, $"通知公告 {command.Id} 不存在");

        mapper.Map(request, entity);
        await noticeRepository.UpdateAsync(entity, cancellationToken);

        // 关键节点发布通知公告更新领域事件（响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new NoticeChangedEvent(command.Id, AggregateChangeType.Updated), cancellationToken);
    }
}
