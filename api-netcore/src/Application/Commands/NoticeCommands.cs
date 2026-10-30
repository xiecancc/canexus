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
public sealed class CreateNoticeCommandHandler : CommandHandler<CreateNoticeCommand, Guid>
{
    private readonly IAggregateRepository<SysNotice> _noticeRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="noticeRepository">通知公告仓储。</param>
    public CreateNoticeCommandHandler(IAggregateRepository<SysNotice> noticeRepository, IMapper mapper)
    {
        _noticeRepository = noticeRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateNoticeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = _mapper.Map<SysNotice>(request);
        var inserted = await _noticeRepository.InsertAsync(entity, cancellationToken);
        return inserted.Id;
    }
}

/// <summary>删除通知公告命令处理器。</summary>
public sealed class DeleteNoticeCommandHandler : CommandHandler<DeleteNoticeCommand>
{
    private readonly IAggregateRepository<SysNotice> _noticeRepository;

    /// <summary>构造函数。</summary>
    /// <param name="noticeRepository">通知公告仓储。</param>
    public DeleteNoticeCommandHandler(IAggregateRepository<SysNotice> noticeRepository)
    {
        _noticeRepository = noticeRepository;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteNoticeCommand command, CancellationToken cancellationToken)
    {
        var entity = await _noticeRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NoticeNotFound, $"通知公告 {command.Id} 不存在");

        await _noticeRepository.DeleteAsync(entity, cancellationToken);
    }
}

/// <summary>更新通知公告命令处理器。</summary>
public sealed class UpdateNoticeCommandHandler : CommandHandler<UpdateNoticeCommand>
{
    private readonly IAggregateRepository<SysNotice> _noticeRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="noticeRepository">通知公告仓储。</param>
    public UpdateNoticeCommandHandler(IAggregateRepository<SysNotice> noticeRepository, IMapper mapper)
    {
        _noticeRepository = noticeRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateNoticeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = await _noticeRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NoticeNotFound, $"通知公告 {command.Id} 不存在");

        _mapper.Map(request, entity);
        await _noticeRepository.UpdateAsync(entity, cancellationToken);
    }
}
