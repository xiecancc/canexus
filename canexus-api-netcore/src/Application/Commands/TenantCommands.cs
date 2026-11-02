
using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Tenants;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Commands;
/// <summary>创建租户命令处理器（校验 tenantKey 唯一；手工构造实体避免映射器引入无关字段）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class TenantCreateCommandHandler(
    IAggregateRepository<SysTenant> tenantRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateCreateCommandHandler<TenantCreateCommand, TenantInputRequest, SysTenant>(tenantRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override async Task ValidateAsync(TenantInputRequest request, CancellationToken cancellationToken)
    {
        var tenantKey = request.TenantKey.Trim();
        await Repository.EnsureNotDuplicateAsync(
            t => t.TenantKey == tenantKey, ErrorCodes.TenantKeyDuplicate,
            $"租户标识「{tenantKey}」已存在", cancellationToken);
    }

    /// <inheritdoc />
    protected override SysTenant MapToEntity(TenantInputRequest request)
    {
        return new SysTenant
        {
            TenantKey = request.TenantKey.Trim(),
            TenantName = request.TenantName.Trim(),
            ContactPhone = request.ContactPhone,
            Status = request.Status,
            Description = request.Description,
            ExpireTime = request.ExpireTime
        };
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysTenant inserted)
    {
        return new TenantCreatedEvent(inserted);
    }
}

/// <summary>删除租户命令处理器（存在归属用户时拒绝删除）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class TenantDeleteCommandHandler(
    IAggregateRepository<SysTenant> tenantRepository,
    IAggregateRepository<SysUser> userRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateDeleteCommandHandler<TenantDeleteCommand, SysTenant>(tenantRepository, domainEventPublisher)
{
    /// <summary>用户仓储（归属用户占用校验）。</summary>
    private IAggregateRepository<SysUser> UserRepository { get; } = userRepository;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.TenantNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "租户";

    /// <inheritdoc />
    protected override async Task ValidateBeforeDeleteAsync(SysTenant entity, CancellationToken cancellationToken)
    {
        // 存在归属用户（未删除数据按租户 Id 归属）时拒绝删除
        if (await UserRepository.CountAsync([u => u.TenantId == entity.Id], cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.TenantInUse, $"租户「{entity.TenantName}」下存在归属用户，不允许删除");
        }
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysTenant deleted)
    {
        return new TenantDeletedEvent(deleted);
    }
}

/// <summary>恢复租户命令处理器（解除软删除墓碑，幂等执行）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class TenantRestoreCommandHandler(
    IAggregateRepository<SysTenant> tenantRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateRestoreCommandHandler<TenantRestoreCommand, SysTenant>(tenantRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysTenant restored)
    {
        return new TenantRestoredEvent(restored);
    }
}

/// <summary>更新租户命令处理器（tenantKey 不可变更；手工赋值保持既有字段更新口径）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class TenantUpdateCommandHandler(
    IAggregateRepository<SysTenant> tenantRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateUpdateCommandHandler<TenantUpdateCommand, TenantInputRequest, SysTenant>(tenantRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.TenantNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "租户";

    /// <inheritdoc />
    protected override Task ValidateAsync(TenantInputRequest request, SysTenant entity, CancellationToken cancellationToken)
    {
        // 租户标识不可变更（与既有值不一致即拒绝，归属关系按 tenantKey 建立）
        var tenantKey = request.TenantKey.Trim();
        if (!string.Equals(tenantKey, entity.TenantKey, StringComparison.Ordinal))
        {
            throw new BusinessException(ErrorCodes.TenantKeyImmutable, "租户标识（tenantKey）不可变更");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void MapToEntity(TenantInputRequest request, SysTenant entity)
    {
        entity.TenantName = request.TenantName.Trim();
        entity.ContactPhone = request.ContactPhone;
        entity.Status = request.Status;
        entity.Description = request.Description;
        entity.ExpireTime = request.ExpireTime;
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysTenant before, SysTenant after)
    {
        return new TenantUpdatedEvent(before, after);
    }
}
