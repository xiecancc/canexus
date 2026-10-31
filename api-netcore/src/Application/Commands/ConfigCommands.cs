namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Configs;
using Application.Mappings;
using AutoMapper;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

/// <summary>创建参数配置命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class CreateConfigCommandHandler(IAggregateRepository<SysConfig> configRepository, IDomainEventPublisher domainEventPublisher, IMapper mapper) : CommandHandler<CreateConfigCommand, Guid>
{
    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateConfigCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var configKey = request.ConfigKey.Trim();
        if (await configRepository.CountAsync(c => c.ConfigKey == configKey, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.ConfigKeyDuplicate, $"参数键名「{configKey}」已存在");
        }

        var entity = mapper.Map<SysConfig>(request);
        var inserted = await configRepository.InsertAsync(entity, cancellationToken);

        // 关键节点发布参数配置创建领域事件（缓存清理等响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new ConfigCreatedEvent(inserted), cancellationToken);
        return inserted.Id;
    }
}

/// <summary>删除参数配置命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DeleteConfigCommandHandler(IAggregateRepository<SysConfig> configRepository, IDomainEventPublisher domainEventPublisher) : CommandHandler<DeleteConfigCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteConfigCommand command, CancellationToken cancellationToken)
    {
        var entity = await configRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.ConfigNotFound, $"参数配置 {command.Id} 不存在");

        if (entity.BuiltIn)
        {
            throw new BusinessException(ErrorCodes.ConfigBuiltInProtected, "内置参数不允许删除");
        }

        // 变更前捕获配置状态快照（软删除可能回填墓碑字段）
        var snapshot = entity.Snapshot<SysConfig>();

        await configRepository.DeleteAsync(entity, cancellationToken);

        // 关键节点发布参数配置删除领域事件（携带删除前的配置状态）
        await domainEventPublisher.PublishAsync(new ConfigDeletedEvent(snapshot), cancellationToken);
    }
}

/// <summary>更新参数配置命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UpdateConfigCommandHandler(IAggregateRepository<SysConfig> configRepository, IDomainEventPublisher domainEventPublisher, IMapper mapper) : CommandHandler<UpdateConfigCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateConfigCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = await configRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.ConfigNotFound, $"参数配置 {command.Id} 不存在");

        var configKey = request.ConfigKey.Trim();
        if (await configRepository.CountAsync(c => c.ConfigKey == configKey && c.Id != command.Id, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.ConfigKeyDuplicate, $"参数键名「{configKey}」已存在");
        }

        // 变更前捕获配置状态快照（键名变更时旧键名由 Before.ConfigKey 表达）
        var before = entity.Snapshot<SysConfig>();

        mapper.Map(request, entity);
        await configRepository.UpdateAsync(entity, cancellationToken);

        // 关键节点发布参数配置修改领域事件（携带变更前后状态，新旧键缓存均由监听器失效）
        await domainEventPublisher.PublishAsync(new ConfigUpdatedEvent(before, entity), cancellationToken);
    }
}
