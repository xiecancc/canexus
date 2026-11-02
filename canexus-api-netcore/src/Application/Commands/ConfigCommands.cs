
using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Configs;
using AutoMapper;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Commands;
/// <summary>创建参数配置命令处理器（校验 configKey 唯一；创建项一律非内置）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class ConfigCreateCommandHandler(
    IAggregateRepository<SysConfig> configRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : AggregateCreateCommandHandler<ConfigCreateCommand, ConfigInputRequest, SysConfig>(configRepository, domainEventPublisher)
{
    /// <summary>AutoMapper 映射器。</summary>
    private IMapper Mapper { get; } = mapper;

    /// <inheritdoc />
    protected override async Task ValidateAsync(ConfigInputRequest request, CancellationToken cancellationToken)
    {
        var configKey = request.ConfigKey.Trim();
        await Repository.EnsureNotDuplicateAsync(
            c => c.ConfigKey == configKey, ErrorCodes.ConfigKeyDuplicate,
            $"参数键名「{configKey}」已存在", cancellationToken);
    }

    /// <inheritdoc />
    protected override SysConfig MapToEntity(ConfigInputRequest request)
    {
        return Mapper.Map<SysConfig>(request);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysConfig inserted)
    {
        return new ConfigCreatedEvent(inserted);
    }
}

/// <summary>删除参数配置命令处理器（内置参数拒绝删除）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class ConfigDeleteCommandHandler(
    IAggregateRepository<SysConfig> configRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateDeleteCommandHandler<ConfigDeleteCommand, SysConfig>(configRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.ConfigNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "参数配置";

    /// <inheritdoc />
    protected override Task ValidateBeforeDeleteAsync(SysConfig entity, CancellationToken cancellationToken)
    {
        if (entity.BuiltIn)
        {
            throw new BusinessException(ErrorCodes.ConfigBuiltInProtected, "内置参数不允许删除");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysConfig deleted)
    {
        return new ConfigDeletedEvent(deleted);
    }
}

/// <summary>恢复参数配置命令处理器（解除软删除墓碑，幂等执行；恢复后缓存由监听器失效）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class ConfigRestoreCommandHandler(
    IAggregateRepository<SysConfig> configRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateRestoreCommandHandler<ConfigRestoreCommand, SysConfig>(configRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysConfig restored)
    {
        return new ConfigRestoredEvent(restored);
    }
}

/// <summary>更新参数配置命令处理器（键名可变更并校验唯一，新旧键缓存均由监听器失效）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class ConfigUpdateCommandHandler(
    IAggregateRepository<SysConfig> configRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : AggregateUpdateCommandHandler<ConfigUpdateCommand, ConfigInputRequest, SysConfig>(configRepository, domainEventPublisher)
{
    /// <summary>AutoMapper 映射器。</summary>
    private IMapper Mapper { get; } = mapper;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.ConfigNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "参数配置";

    /// <inheritdoc />
    protected override async Task ValidateAsync(ConfigInputRequest request, SysConfig entity, CancellationToken cancellationToken)
    {
        var configKey = request.ConfigKey.Trim();
        await Repository.EnsureNotDuplicateAsync(
            c => c.ConfigKey == configKey && c.Id != entity.Id, ErrorCodes.ConfigKeyDuplicate,
            $"参数键名「{configKey}」已存在", cancellationToken);
    }

    /// <inheritdoc />
    protected override void MapToEntity(ConfigInputRequest request, SysConfig entity)
    {
        Mapper.Map(request, entity);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysConfig before, SysConfig after)
    {
        return new ConfigUpdatedEvent(before, after);
    }
}
