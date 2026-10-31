namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Dicts;
using AutoMapper;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

/// <summary>创建字典数据命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class CreateDictDataCommandHandler(
    IAggregateRepository<SysDictData> dictDataRepository,
    IAggregateRepository<SysDictType> dictTypeRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : CommandHandler<CreateDictDataCommand, Guid>
{
    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateDictDataCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var dictType = request.DictType.Trim();
        await EnsureTypeExistsAsync(dictType, cancellationToken);
        await EnsureNotDuplicateAsync(dictType, request.DictLabel.Trim(), request.DictValue.Trim(), excludeId: null, cancellationToken);

        var entity = mapper.Map<SysDictData>(request);
        var inserted = await dictDataRepository.InsertAsync(entity, cancellationToken);

        // 关键节点发布字典数据创建领域事件（缓存清理等响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new DictDataChangedEvent(inserted.Id, dictType, AggregateChangeType.Created), cancellationToken);
        return inserted.Id;
    }

    /// <summary>校验所属字典类型存在。</summary>
    private async Task EnsureTypeExistsAsync(string dictType, CancellationToken cancellationToken)
    {
        if (await dictTypeRepository.CountAsync(t => t.DictType == dictType, cancellationToken) == 0)
        {
            throw new BusinessException(ErrorCodes.DictTypeNotFound, $"字典类型「{dictType}」不存在");
        }
    }

    /// <summary>校验同字典类型下标签 / 键值未被占用（更新时排除自身）。</summary>
    private async Task EnsureNotDuplicateAsync(string dictType, string label, string value, Guid? excludeId, CancellationToken cancellationToken)
    {
        System.Linq.Expressions.Expression<Func<SysDictData, bool>> labelPredicate = excludeId == null
            ? d => d.DictType == dictType && d.DictLabel == label
            : d => d.DictType == dictType && d.DictLabel == label && d.Id != excludeId.Value;
        if (await dictDataRepository.CountAsync(labelPredicate, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DictDataDuplicate, $"字典类型「{dictType}」下标签「{label}」已存在");
        }

        System.Linq.Expressions.Expression<Func<SysDictData, bool>> valuePredicate = excludeId == null
            ? d => d.DictType == dictType && d.DictValue == value
            : d => d.DictType == dictType && d.DictValue == value && d.Id != excludeId.Value;
        if (await dictDataRepository.CountAsync(valuePredicate, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DictDataDuplicate, $"字典类型「{dictType}」下键值「{value}」已存在");
        }
    }
}

/// <summary>创建字典类型命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class CreateDictTypeCommandHandler(
    IAggregateRepository<SysDictType> dictTypeRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : CommandHandler<CreateDictTypeCommand, Guid>
{
    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateDictTypeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var dictType = request.DictType.Trim();
        if (await dictTypeRepository.CountAsync(t => t.DictType == dictType, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DictTypeDuplicate, $"字典类型「{dictType}」已存在");
        }

        var entity = mapper.Map<SysDictType>(request);
        var inserted = await dictTypeRepository.InsertAsync(entity, cancellationToken);

        // 关键节点发布字典类型创建领域事件（清理等响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new DictTypeChangedEvent(inserted.Id, AggregateChangeType.Created), cancellationToken);
        return inserted.Id;
    }
}

/// <summary>删除字典数据命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DeleteDictDataCommandHandler(
    IAggregateRepository<SysDictData> dictDataRepository,
    IDomainEventPublisher domainEventPublisher) : CommandHandler<DeleteDictDataCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteDictDataCommand command, CancellationToken cancellationToken)
    {
        var entity = await dictDataRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DictDataNotFound, $"字典数据 {command.Id} 不存在");

        await dictDataRepository.DeleteAsync(entity, cancellationToken);

        // 关键节点发布字典数据删除领域事件（缓存清理等响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new DictDataChangedEvent(command.Id, entity.DictType, AggregateChangeType.Deleted), cancellationToken);
    }
}

/// <summary>删除字典类型命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DeleteDictTypeCommandHandler(
    IAggregateRepository<SysDictType> dictTypeRepository,
    IAggregateRepository<SysDictData> dictDataRepository,
    IDomainEventPublisher domainEventPublisher) : CommandHandler<DeleteDictTypeCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteDictTypeCommand command, CancellationToken cancellationToken)
    {
        var entity = await dictTypeRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DictTypeNotFound, $"字典类型 {command.Id} 不存在");

        if (await dictDataRepository.CountAsync(d => d.DictType == entity.DictType, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DictTypeHasData, "该字典类型下存在字典数据，不允许删除");
        }

        await dictTypeRepository.DeleteAsync(entity, cancellationToken);

        // 关键节点发布字典类型删除领域事件（清理等响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new DictTypeChangedEvent(command.Id, AggregateChangeType.Deleted), cancellationToken);
    }
}

/// <summary>更新字典数据命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UpdateDictDataCommandHandler(
    IAggregateRepository<SysDictData> dictDataRepository,
    IAggregateRepository<SysDictType> dictTypeRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : CommandHandler<UpdateDictDataCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateDictDataCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = await dictDataRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DictDataNotFound, $"字典数据 {command.Id} 不存在");

        var dictType = request.DictType.Trim();
        if (!string.Equals(dictType, entity.DictType, StringComparison.Ordinal))
        {
            if (await dictTypeRepository.CountAsync(t => t.DictType == dictType, cancellationToken) == 0)
            {
                throw new BusinessException(ErrorCodes.DictTypeNotFound, $"字典类型「{dictType}」不存在");
            }
        }

        await EnsureNotDuplicateAsync(dictType, request.DictLabel.Trim(), request.DictValue.Trim(), excludeId: command.Id, cancellationToken);

        var originalType = entity.DictType;
        mapper.Map(request, entity);
        await dictDataRepository.UpdateAsync(entity, cancellationToken);

        // 关键节点发布字典数据更新领域事件（类型迁移时新旧类型缓存均由监听器失效）
        await domainEventPublisher.PublishAsync(
            new DictDataChangedEvent(command.Id, dictType, AggregateChangeType.Updated, originalType), cancellationToken);
    }

    /// <summary>校验同字典类型下标签 / 键值未被占用（更新时排除自身）。</summary>
    private async Task EnsureNotDuplicateAsync(string dictType, string label, string value, Guid? excludeId, CancellationToken cancellationToken)
    {
        System.Linq.Expressions.Expression<Func<SysDictData, bool>> labelPredicate = excludeId == null
            ? d => d.DictType == dictType && d.DictLabel == label
            : d => d.DictType == dictType && d.DictLabel == label && d.Id != excludeId.Value;
        if (await dictDataRepository.CountAsync(labelPredicate, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DictDataDuplicate, $"字典类型「{dictType}」下标签「{label}」已存在");
        }

        System.Linq.Expressions.Expression<Func<SysDictData, bool>> valuePredicate = excludeId == null
            ? d => d.DictType == dictType && d.DictValue == value
            : d => d.DictType == dictType && d.DictValue == value && d.Id != excludeId.Value;
        if (await dictDataRepository.CountAsync(valuePredicate, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DictDataDuplicate, $"字典类型「{dictType}」下键值「{value}」已存在");
        }
    }
}

/// <summary>更新字典类型命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UpdateDictTypeCommandHandler(
    IAggregateRepository<SysDictType> dictTypeRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : CommandHandler<UpdateDictTypeCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateDictTypeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = await dictTypeRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DictTypeNotFound, $"字典类型 {command.Id} 不存在");

        var dictType = request.DictType.Trim();
        if (!string.Equals(dictType, entity.DictType, StringComparison.Ordinal))
        {
            throw new BusinessException(ErrorCodes.DictTypeImmutable, "字典类型（dictType）不可变更");
        }

        mapper.Map(request, entity);
        await dictTypeRepository.UpdateAsync(entity, cancellationToken);

        // 关键节点发布字典类型更新领域事件（清理等响应动作由监听器处理）
        await domainEventPublisher.PublishAsync(new DictTypeChangedEvent(command.Id, AggregateChangeType.Updated), cancellationToken);
    }
}
