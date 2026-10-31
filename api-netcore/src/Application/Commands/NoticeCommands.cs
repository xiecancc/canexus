
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

namespace Application.Commands;
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
        await domainEventPublisher.PublishAsync(new NoticeCreatedEvent(inserted), cancellationToken);
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
        var entity = await noticeRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NoticeNotFound, $"通知公告 {command.Id} 不存在");

        // 变更前捕获公告状态快照（软删除可能回填墓碑字段）
        var snapshot = entity.Snapshot<SysNotice>();

        await noticeRepository.DeleteAsync(entity, cancellationToken);

        // 关键节点发布通知公告删除领域事件（响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new NoticeDeletedEvent(snapshot), cancellationToken);
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
        var entity = await noticeRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NoticeNotFound, $"通知公告 {command.Id} 不存在");

        // 变更前捕获公告状态快照（实体即将被原位修改）
        var before = entity.Snapshot<SysNotice>();

        mapper.Map(request, entity);
        await noticeRepository.UpdateAsync(entity, cancellationToken);

        // 关键节点发布通知公告修改领域事件（携带变更前后状态）
        await domainEventPublisher.PublishAsync(new NoticeUpdatedEvent(before, entity), cancellationToken);
    }
}
