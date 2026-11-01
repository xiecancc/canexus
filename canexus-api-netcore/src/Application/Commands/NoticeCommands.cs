
using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Notices;
using AutoMapper;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Results;

namespace Application.Commands;
/// <summary>创建通知公告命令处理器（标准创建流程，无前置业务校验——字段规则由验证器承接）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class NoticeCreateCommandHandler(
    IAggregateRepository<SysNotice> noticeRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : DomainCreateCommandHandler<NoticeCreateCommand, NoticeCreateRequest, SysNotice>(noticeRepository, domainEventPublisher)
{
    /// <summary>AutoMapper 映射器。</summary>
    private IMapper Mapper { get; } = mapper;

    /// <inheritdoc />
    protected override Task ValidateAsync(NoticeCreateRequest request, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override SysNotice MapToEntity(NoticeCreateRequest request)
    {
        return Mapper.Map<SysNotice>(request);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysNotice inserted)
    {
        return new NoticeCreatedEvent(inserted);
    }
}

/// <summary>删除通知公告命令处理器（标准软删除流程，无删除前守卫）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class NoticeDeleteCommandHandler(
    IAggregateRepository<SysNotice> noticeRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateDeleteCommandHandler<NoticeDeleteCommand, SysNotice>(noticeRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.NoticeNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "通知公告";

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysNotice deleted)
    {
        return new NoticeDeletedEvent(deleted);
    }
}

/// <summary>更新通知公告命令处理器（标准原位更新流程，无前置业务校验——字段规则由验证器承接）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class NoticeUpdateCommandHandler(
    IAggregateRepository<SysNotice> noticeRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : AggregateUpdateCommandHandler<NoticeUpdateCommand, NoticeUpdateRequest, SysNotice>(noticeRepository, domainEventPublisher)
{
    /// <summary>AutoMapper 映射器。</summary>
    private IMapper Mapper { get; } = mapper;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.NoticeNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "通知公告";

    /// <inheritdoc />
    protected override Task ValidateAsync(NoticeUpdateRequest request, SysNotice entity, CancellationToken cancellationToken)
    {
        // 无业务守卫：字段规则由验证器拦截，乐观锁比对由基类统一执行
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void MapToEntity(NoticeUpdateRequest request, SysNotice entity)
    {
        Mapper.Map(request, entity);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysNotice before, SysNotice after)
    {
        return new NoticeUpdatedEvent(before, after);
    }
}
