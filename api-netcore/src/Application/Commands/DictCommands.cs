namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Dicts;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Rules;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.Shared.Caching;

/// <summary>创建字典数据命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="dictDataRepository">字典数据仓储。</param>
/// <param name="dictTypeRepository">字典类型仓储（存在性校验）。</param>
/// <param name="cache">内存缓存（写后失效对应类型缓存）。</param>
/// <param name="mapper">DTO → 实体映射。</param>
public sealed class CreateDictDataCommandHandler(
    IAggregateRepository<SysDictData> dictDataRepository,
    IAggregateRepository<SysDictType> dictTypeRepository,
    ICache cache,
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
        await cache.RemoveAsync(DictRules.DataTypeCacheKey(dictType));
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
/// <param name="dictTypeRepository">字典类型仓储。</param>
/// <param name="mapper">DTO → 实体映射。</param>
public sealed class CreateDictTypeCommandHandler(IAggregateRepository<SysDictType> dictTypeRepository, IMapper mapper) : CommandHandler<CreateDictTypeCommand, Guid>
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
        return inserted.Id;
    }
}

/// <summary>删除字典数据命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="dictDataRepository">字典数据仓储。</param>
/// <param name="cache">内存缓存（删除后失效对应类型缓存）。</param>
public sealed class DeleteDictDataCommandHandler(IAggregateRepository<SysDictData> dictDataRepository, ICache cache) : CommandHandler<DeleteDictDataCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteDictDataCommand command, CancellationToken cancellationToken)
    {
        var entity = await dictDataRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DictDataNotFound, $"字典数据 {command.Id} 不存在");

        await dictDataRepository.DeleteAsync(entity, cancellationToken);
        await cache.RemoveAsync(DictRules.DataTypeCacheKey(entity.DictType));
    }
}

/// <summary>删除字典类型命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="dictTypeRepository">字典类型仓储。</param>
/// <param name="dictDataRepository">字典数据仓储（删除前置校验）。</param>
public sealed class DeleteDictTypeCommandHandler(
    IAggregateRepository<SysDictType> dictTypeRepository,
    IAggregateRepository<SysDictData> dictDataRepository) : CommandHandler<DeleteDictTypeCommand>
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
    }
}

/// <summary>更新字典数据命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="dictDataRepository">字典数据仓储。</param>
/// <param name="dictTypeRepository">字典类型仓储（类型迁移时存在性校验）。</param>
/// <param name="cache">内存缓存（写后失效新旧类型缓存）。</param>
/// <param name="mapper">DTO → 实体映射。</param>
public sealed class UpdateDictDataCommandHandler(
    IAggregateRepository<SysDictData> dictDataRepository,
    IAggregateRepository<SysDictType> dictTypeRepository,
    ICache cache,
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

        // 类型迁移时新旧类型缓存均失效，保证两个维度都立即读到最新数据
        await cache.RemoveAsync(DictRules.DataTypeCacheKey(dictType));
        if (!string.Equals(originalType, dictType, StringComparison.Ordinal))
        {
            await cache.RemoveAsync(DictRules.DataTypeCacheKey(originalType));
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

/// <summary>更新字典类型命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="dictTypeRepository">字典类型仓储。</param>
/// <param name="mapper">DTO → 实体映射。</param>
public sealed class UpdateDictTypeCommandHandler(IAggregateRepository<SysDictType> dictTypeRepository, IMapper mapper) : CommandHandler<UpdateDictTypeCommand>
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
    }
}
