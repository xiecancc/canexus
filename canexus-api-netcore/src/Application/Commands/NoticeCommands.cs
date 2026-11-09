
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
/// <param name="noticeRepository">公告聚合仓储。</param>
/// <param name="domainEventPublisher">领域事件发布器。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
/// <param name="htmlSanitizer">富文本净化器（公告正文入库前防 XSS）。</param>
public sealed class NoticeCreateCommandHandler(
    IAggregateRepository<SysNotice> noticeRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper,
    Security.HtmlRichTextSanitizer htmlSanitizer) : AggregateCreateCommandHandler<NoticeCreateCommand, NoticeInputRequest, SysNotice>(noticeRepository, domainEventPublisher)
{
    /// <summary>AutoMapper 映射器。</summary>
    private IMapper Mapper { get; } = mapper;

    /// <summary>富文本净化器。</summary>
    private Security.HtmlRichTextSanitizer HtmlSanitizer { get; } = htmlSanitizer;

    /// <inheritdoc />
    protected override Task ValidateAsync(NoticeInputRequest request, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override SysNotice MapToEntity(NoticeInputRequest request)
    {
        var entity = Mapper.Map<SysNotice>(request);

        // 公告正文为富文本（前端可提交任意 HTML）：入库前白名单净化，防存储型 XSS
        entity.Content = HtmlSanitizer.Sanitize(entity.Content);
        return entity;
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

/// <summary>恢复通知公告命令处理器（解除软删除墓碑，幂等执行）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class NoticeRestoreCommandHandler(
    IAggregateRepository<SysNotice> noticeRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateRestoreCommandHandler<NoticeRestoreCommand, SysNotice>(noticeRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysNotice restored)
    {
        return new NoticeRestoredEvent(restored);
    }
}

/// <summary>更新通知公告命令处理器（标准原位更新流程，无前置业务校验——字段规则由验证器承接）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="noticeRepository">公告聚合仓储。</param>
/// <param name="domainEventPublisher">领域事件发布器。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
/// <param name="htmlSanitizer">富文本净化器（公告正文入库前防 XSS）。</param>
public sealed class NoticeUpdateCommandHandler(
    IAggregateRepository<SysNotice> noticeRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper,
    Security.HtmlRichTextSanitizer htmlSanitizer) : AggregateUpdateCommandHandler<NoticeUpdateCommand, NoticeInputRequest, SysNotice>(noticeRepository, domainEventPublisher)
{
    /// <summary>AutoMapper 映射器。</summary>
    private IMapper Mapper { get; } = mapper;

    /// <summary>富文本净化器。</summary>
    private Security.HtmlRichTextSanitizer HtmlSanitizer { get; } = htmlSanitizer;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.NoticeNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "通知公告";

    /// <inheritdoc />
    protected override Task ValidateAsync(NoticeInputRequest request, SysNotice entity, CancellationToken cancellationToken)
    {
        // 无业务守卫：字段规则由验证器拦截，乐观锁比对由基类统一执行
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void MapToEntity(NoticeInputRequest request, SysNotice entity)
    {
        Mapper.Map(request, entity);

        // 公告正文为富文本（前端可提交任意 HTML）：入库前白名单净化，防存储型 XSS
        entity.Content = HtmlSanitizer.Sanitize(entity.Content);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysNotice before, SysNotice after)
    {
        return new NoticeUpdatedEvent(before, after);
    }
}
