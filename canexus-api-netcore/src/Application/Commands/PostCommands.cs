
using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Posts;
using AutoMapper;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Commands;
/// <summary>创建岗位命令处理器（编码唯一性校验 → 标准创建流程）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class PostCreateCommandHandler(
    IAggregateRepository<SysPost> postRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : AggregateCreateCommandHandler<PostCreateCommand, PostInputRequest, SysPost>(postRepository, domainEventPublisher)
{
    /// <summary>AutoMapper 映射器。</summary>
    private IMapper Mapper { get; } = mapper;

    /// <inheritdoc />
    protected override async Task ValidateAsync(PostInputRequest request, CancellationToken cancellationToken)
    {
        // 岗位编码唯一性（含软删除墓碑一并排除——业务层在非删除集上校验，与用户名口径一致）
        await Repository.EnsureNotDuplicateAsync(
            p => p.PostCode == request.PostCode,
            ErrorCodes.PostCodeDuplicate,
            $"岗位编码 {request.PostCode} 已存在",
            cancellationToken);
    }

    /// <inheritdoc />
    protected override SysPost MapToEntity(PostInputRequest request)
    {
        return Mapper.Map<SysPost>(request);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysPost inserted)
    {
        return new PostCreatedEvent(inserted);
    }
}

/// <summary>删除岗位命令处理器（删除前守卫：岗位仍被用户引用时拒绝删除，避免关联悬挂）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class PostDeleteCommandHandler(
    IAggregateRepository<SysPost> postRepository,
    IDomainRepository<SysUserPost> userPostRepository,
    IDomainEventPublisher domainEventPublisher)
    : AggregateDeleteCommandHandler<PostDeleteCommand, SysPost>(postRepository, domainEventPublisher)
{
    /// <summary>用户-岗位关联仓储（引用计数守卫）。</summary>
    private IDomainRepository<SysUserPost> UserPostRepository { get; } = userPostRepository;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.PostNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "岗位";

    /// <inheritdoc />
    protected override async Task ValidateBeforeDeleteAsync(SysPost entity, CancellationToken cancellationToken)
    {
        if (await UserPostRepository.CountAsync([ur => ur.PostId == entity.Id], cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.PostInUse, $"岗位 {entity.PostName} 仍被用户引用，请先解除用户-岗位关联");
        }
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysPost deleted)
    {
        return new PostDeletedEvent(deleted);
    }
}

/// <summary>恢复岗位命令处理器（解除软删除墓碑，幂等执行）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class PostRestoreCommandHandler(
    IAggregateRepository<SysPost> postRepository,
    IDomainEventPublisher domainEventPublisher)
    : AggregateRestoreCommandHandler<PostRestoreCommand, SysPost>(postRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysPost restored)
    {
        return new PostRestoredEvent(restored);
    }
}

/// <summary>更新岗位命令处理器（编码唯一性校验排除自身 → 标准原位更新流程）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class PostUpdateCommandHandler(
    IAggregateRepository<SysPost> postRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : AggregateUpdateCommandHandler<PostUpdateCommand, PostInputRequest, SysPost>(postRepository, domainEventPublisher)
{
    /// <summary>AutoMapper 映射器。</summary>
    private IMapper Mapper { get; } = mapper;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.PostNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "岗位";

    /// <inheritdoc />
    protected override async Task ValidateAsync(PostInputRequest request, SysPost entity, CancellationToken cancellationToken)
    {
        if (entity.PostCode != request.PostCode)
        {
            await Repository.EnsureNotDuplicateAsync(
                p => p.PostCode == request.PostCode,
                ErrorCodes.PostCodeDuplicate,
                $"岗位编码 {request.PostCode} 已存在",
                cancellationToken);
        }
    }

    /// <inheritdoc />
    protected override void MapToEntity(PostInputRequest request, SysPost entity)
    {
        Mapper.Map(request, entity);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysPost before, SysPost after)
    {
        return new PostUpdatedEvent(before, after);
    }
}
