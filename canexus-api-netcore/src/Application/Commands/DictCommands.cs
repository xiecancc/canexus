
using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Dicts;
using AutoMapper;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Commands;
/// <summary>字典数据命令的共享校验逻辑（字典类型存在性与同类型下标签 / 键值唯一性）。</summary>
internal static class DictDataRules
{
    /// <summary>校验所属字典类型存在。</summary>
    public static async Task EnsureTypeExistsAsync(IAggregateRepository<SysDictType> dictTypeRepository, string dictType, CancellationToken cancellationToken)
    {
        if (await dictTypeRepository.CountAsync([t => t.DictType == dictType], cancellationToken) == 0)
        {
            throw new BusinessException(ErrorCodes.DictTypeNotFound, $"字典类型「{dictType}」不存在");
        }
    }

    /// <summary>校验同字典类型下标签 / 键值未被占用（excludeId 非空时排除自身，用于更新场景）。</summary>
    public static async Task EnsureNotDuplicateAsync(IAggregateRepository<SysDictData> dictDataRepository, string dictType, string label, string value, Guid? excludeId, CancellationToken cancellationToken)
    {
        Expression<Func<SysDictData, bool>> labelPredicate = excludeId is null
            ? d => d.DictType == dictType && d.DictLabel == label
            : d => d.DictType == dictType && d.DictLabel == label && d.Id != excludeId.Value;
        await dictDataRepository.EnsureNotDuplicateAsync(
            labelPredicate, ErrorCodes.DictDataDuplicate, $"字典类型「{dictType}」下标签「{label}」已存在", cancellationToken);

        Expression<Func<SysDictData, bool>> valuePredicate = excludeId is null
            ? d => d.DictType == dictType && d.DictValue == value
            : d => d.DictType == dictType && d.DictValue == value && d.Id != excludeId.Value;
        await dictDataRepository.EnsureNotDuplicateAsync(
            valuePredicate, ErrorCodes.DictDataDuplicate, $"字典类型「{dictType}」下键值「{value}」已存在", cancellationToken);
    }
}

/// <summary>创建字典数据命令处理器（校验类型存在与同类型下标签 / 键值唯一）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DictDataCreateCommandHandler(
    IAggregateRepository<SysDictData> dictDataRepository,
    IAggregateRepository<SysDictType> dictTypeRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : AggregateCreateCommandHandler<DictDataCreateCommand, DictDataInputRequest, SysDictData>(dictDataRepository, domainEventPublisher)
{
    /// <summary>字典类型仓储（类型存在性校验）。</summary>
    private IAggregateRepository<SysDictType> DictTypeRepository { get; } = dictTypeRepository;

    /// <summary>AutoMapper 映射器。</summary>
    private IMapper Mapper { get; } = mapper;

    /// <inheritdoc />
    protected override async Task ValidateAsync(DictDataInputRequest request, CancellationToken cancellationToken)
    {
        var dictType = request.DictType.Trim();
        await DictDataRules.EnsureTypeExistsAsync(DictTypeRepository, dictType, cancellationToken);
        await DictDataRules.EnsureNotDuplicateAsync(
            Repository, dictType, request.DictLabel.Trim(), request.DictValue.Trim(), excludeId: null, cancellationToken);
    }

    /// <inheritdoc />
    protected override SysDictData MapToEntity(DictDataInputRequest request)
    {
        return Mapper.Map<SysDictData>(request);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysDictData inserted)
    {
        return new DictDataCreatedEvent(inserted);
    }
}

/// <summary>创建字典类型命令处理器（校验 dictType 全局唯一）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DictTypeCreateCommandHandler(
    IAggregateRepository<SysDictType> dictTypeRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : AggregateCreateCommandHandler<DictTypeCreateCommand, DictTypeInputRequest, SysDictType>(dictTypeRepository, domainEventPublisher)
{
    /// <summary>AutoMapper 映射器。</summary>
    private IMapper Mapper { get; } = mapper;

    /// <inheritdoc />
    protected override async Task ValidateAsync(DictTypeInputRequest request, CancellationToken cancellationToken)
    {
        var dictType = request.DictType.Trim();
        await Repository.EnsureNotDuplicateAsync(
            t => t.DictType == dictType, ErrorCodes.DictTypeDuplicate,
            $"字典类型「{dictType}」已存在", cancellationToken);
    }

    /// <inheritdoc />
    protected override SysDictType MapToEntity(DictTypeInputRequest request)
    {
        return Mapper.Map<SysDictType>(request);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysDictType inserted)
    {
        return new DictTypeCreatedEvent(inserted);
    }
}

/// <summary>删除字典数据命令处理器（标准软删除流程，失效所属类型缓存由监听器处理）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DictDataDeleteCommandHandler(
    IAggregateRepository<SysDictData> dictDataRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateDeleteCommandHandler<DictDataDeleteCommand, SysDictData>(dictDataRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.DictDataNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "字典数据";

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysDictData deleted)
    {
        return new DictDataDeletedEvent(deleted);
    }
}

/// <summary>恢复字典数据命令处理器（解除软删除墓碑，幂等执行；所属类型缓存由监听器失效）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DictDataRestoreCommandHandler(
    IAggregateRepository<SysDictData> dictDataRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateRestoreCommandHandler<DictDataRestoreCommand, SysDictData>(dictDataRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysDictData restored)
    {
        return new DictDataRestoredEvent(restored);
    }
}

/// <summary>删除字典类型命令处理器（存在字典数据时拒绝删除，保证不留孤儿数据）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DictTypeDeleteCommandHandler(
    IAggregateRepository<SysDictType> dictTypeRepository,
    IAggregateRepository<SysDictData> dictDataRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateDeleteCommandHandler<DictTypeDeleteCommand, SysDictType>(dictTypeRepository, domainEventPublisher)
{
    /// <summary>字典数据仓储（子数据占用校验）。</summary>
    private IAggregateRepository<SysDictData> DictDataRepository { get; } = dictDataRepository;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.DictTypeNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "字典类型";

    /// <inheritdoc />
    protected override async Task ValidateBeforeDeleteAsync(SysDictType entity, CancellationToken cancellationToken)
    {
        if (await DictDataRepository.CountAsync([d => d.DictType == entity.DictType], cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DictTypeHasData, "该字典类型下存在字典数据，不允许删除");
        }
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysDictType deleted)
    {
        return new DictTypeDeletedEvent(deleted);
    }
}

/// <summary>恢复字典类型命令处理器（解除软删除墓碑，幂等执行）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DictTypeRestoreCommandHandler(
    IAggregateRepository<SysDictType> dictTypeRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateRestoreCommandHandler<DictTypeRestoreCommand, SysDictType>(dictTypeRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysDictType restored)
    {
        return new DictTypeRestoredEvent(restored);
    }
}

/// <summary>更新字典数据命令处理器（类型可迁移，迁移后新旧类型缓存均由监听器失效）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DictDataUpdateCommandHandler(
    IAggregateRepository<SysDictData> dictDataRepository,
    IAggregateRepository<SysDictType> dictTypeRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : AggregateUpdateCommandHandler<DictDataUpdateCommand, DictDataInputRequest, SysDictData>(dictDataRepository, domainEventPublisher)
{
    /// <summary>字典类型仓储（迁移目标类型存在性校验）。</summary>
    private IAggregateRepository<SysDictType> DictTypeRepository { get; } = dictTypeRepository;

    /// <summary>AutoMapper 映射器。</summary>
    private IMapper Mapper { get; } = mapper;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.DictDataNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "字典数据";

    /// <inheritdoc />
    protected override async Task ValidateAsync(DictDataInputRequest request, SysDictData entity, CancellationToken cancellationToken)
    {
        var dictType = request.DictType.Trim();
        if (!string.Equals(dictType, entity.DictType, StringComparison.Ordinal))
        {
            await DictDataRules.EnsureTypeExistsAsync(DictTypeRepository, dictType, cancellationToken);
        }

        await DictDataRules.EnsureNotDuplicateAsync(
            Repository, dictType, request.DictLabel.Trim(), request.DictValue.Trim(), excludeId: entity.Id, cancellationToken);
    }

    /// <inheritdoc />
    protected override void MapToEntity(DictDataInputRequest request, SysDictData entity)
    {
        Mapper.Map(request, entity);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysDictData before, SysDictData after)
    {
        return new DictDataUpdatedEvent(before, after);
    }
}

/// <summary>更新字典类型命令处理器（dictType 不可变更）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DictTypeUpdateCommandHandler(
    IAggregateRepository<SysDictType> dictTypeRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : AggregateUpdateCommandHandler<DictTypeUpdateCommand, DictTypeInputRequest, SysDictType>(dictTypeRepository, domainEventPublisher)
{
    /// <summary>AutoMapper 映射器。</summary>
    private IMapper Mapper { get; } = mapper;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.DictTypeNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "字典类型";

    /// <inheritdoc />
    protected override Task ValidateAsync(DictTypeInputRequest request, SysDictType entity, CancellationToken cancellationToken)
    {
        var dictType = request.DictType.Trim();
        if (!string.Equals(dictType, entity.DictType, StringComparison.Ordinal))
        {
            throw new BusinessException(ErrorCodes.DictTypeImmutable, "字典类型（dictType）不可变更");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void MapToEntity(DictTypeInputRequest request, SysDictType entity)
    {
        Mapper.Map(request, entity);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysDictType before, SysDictType after)
    {
        return new DictTypeUpdatedEvent(before, after);
    }
}
