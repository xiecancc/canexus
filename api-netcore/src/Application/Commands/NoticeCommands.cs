namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Notices;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Rules;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

/// <summary>创建通知公告命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="noticeRepository">通知公告仓储。</param>
public sealed class CreateNoticeCommandHandler(IAggregateRepository<SysNotice> noticeRepository, IMapper mapper) : CommandHandler<CreateNoticeCommand, Guid>
{
    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateNoticeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = mapper.Map<SysNotice>(request);
        var inserted = await noticeRepository.InsertAsync(entity, cancellationToken);
        return inserted.Id;
    }
}

/// <summary>删除通知公告命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="noticeRepository">通知公告仓储。</param>
public sealed class DeleteNoticeCommandHandler(IAggregateRepository<SysNotice> noticeRepository) : CommandHandler<DeleteNoticeCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteNoticeCommand command, CancellationToken cancellationToken)
    {
        var entity = await noticeRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NoticeNotFound, $"通知公告 {command.Id} 不存在");

        await noticeRepository.DeleteAsync(entity, cancellationToken);
    }
}

/// <summary>更新通知公告命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="noticeRepository">通知公告仓储。</param>
public sealed class UpdateNoticeCommandHandler(IAggregateRepository<SysNotice> noticeRepository, IMapper mapper) : CommandHandler<UpdateNoticeCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateNoticeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = await noticeRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NoticeNotFound, $"通知公告 {command.Id} 不存在");

        mapper.Map(request, entity);
        await noticeRepository.UpdateAsync(entity, cancellationToken);
    }
}
