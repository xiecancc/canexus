namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Tenants;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

/// <summary>创建租户命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class CreateTenantCommandHandler(IAggregateRepository<SysTenant> tenantRepository, IDomainEventPublisher domainEventPublisher) : CommandHandler<CreateTenantCommand, Guid>
{
    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateTenantCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var tenantKey = request.TenantKey.Trim();
        var tenantName = request.TenantName.Trim();

        if (await tenantRepository.CountAsync(t => t.TenantKey == tenantKey, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.TenantKeyDuplicate, $"租户标识「{tenantKey}」已存在");
        }

        var entity = new SysTenant
        {
            TenantKey = tenantKey,
            TenantName = tenantName,
            ContactPhone = request.ContactPhone,
            Status = request.Status,
            Description = request.Description,
            ExpireTime = request.ExpireTime
        };
        var inserted = await tenantRepository.InsertAsync(entity, cancellationToken);

        // 关键节点发布租户创建领域事件（租户初始化等响应动作可由监听器扩展处理）
        await domainEventPublisher.PublishAsync(new TenantChangedEvent(inserted.Id, AggregateChangeType.Created), cancellationToken);
        return inserted.Id;
    }
}

/// <summary>删除租户命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DeleteTenantCommandHandler(
    IAggregateRepository<SysTenant> tenantRepository,
    IAggregateRepository<SysUser> userRepository,
    IDomainEventPublisher domainEventPublisher) : CommandHandler<DeleteTenantCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteTenantCommand command, CancellationToken cancellationToken)
    {
        var entity = await tenantRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.TenantNotFound, $"租户 {command.Id} 不存在");

        // 存在归属用户（未删除数据按租户 Id 归属）时拒绝删除
        if (await userRepository.CountAsync(u => u.TenantId == entity.Id, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.TenantInUse, $"租户「{entity.TenantName}」下存在归属用户，不允许删除");
        }

        await tenantRepository.DeleteAsync(entity, cancellationToken);

        // 关键节点发布租户删除领域事件（响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new TenantChangedEvent(command.Id, AggregateChangeType.Deleted), cancellationToken);
    }
}

/// <summary>更新租户命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UpdateTenantCommandHandler(IAggregateRepository<SysTenant> tenantRepository, IDomainEventPublisher domainEventPublisher) : CommandHandler<UpdateTenantCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateTenantCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = await tenantRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.TenantNotFound, $"租户 {command.Id} 不存在");

        var tenantKey = request.TenantKey.Trim();
        var tenantName = request.TenantName.Trim();

        // 租户标识不可变更（与既有值不一致即拒绝，归属关系按 tenantKey 建立）
        if (!string.Equals(tenantKey, entity.TenantKey, StringComparison.Ordinal))
        {
            throw new BusinessException(ErrorCodes.TenantKeyImmutable, "租户标识（tenantKey）不可变更");
        }

        entity.TenantName = tenantName;
        entity.ContactPhone = request.ContactPhone;
        entity.Status = request.Status;
        entity.Description = request.Description;
        entity.ExpireTime = request.ExpireTime;
        await tenantRepository.UpdateAsync(entity, cancellationToken);

        // 关键节点发布租户更新领域事件（响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new TenantChangedEvent(command.Id, AggregateChangeType.Updated), cancellationToken);
    }
}
