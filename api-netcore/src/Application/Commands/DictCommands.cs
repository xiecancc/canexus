namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Dicts;
using AutoMapper;
using Infrastructure.Shared.Caching;
using Domain.Services.Rules;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

/// <summary>创建字典数据命令处理器。</summary>
public sealed class CreateDictDataCommandHandler : CommandHandler<CreateDictDataCommand, Guid>
{
    private readonly IAggregateRepository<SysDictData> _dictDataRepository;
    private readonly IAggregateRepository<SysDictType> _dictTypeRepository;
    private readonly ICache _cache;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="dictDataRepository">字典数据仓储。</param>
    /// <param name="dictTypeRepository">字典类型仓储（存在性校验）。</param>
    /// <param name="cache">内存缓存（写后失效对应类型缓存）。</param>
    /// <param name="mapper">DTO → 实体映射。</param>
    public CreateDictDataCommandHandler(
        IAggregateRepository<SysDictData> dictDataRepository,
        IAggregateRepository<SysDictType> dictTypeRepository,
        ICache cache,
        IMapper mapper)
    {
        _dictDataRepository = dictDataRepository;
        _dictTypeRepository = dictTypeRepository;
        _cache = cache;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateDictDataCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var dictType = request.DictType.Trim();
        await EnsureTypeExistsAsync(dictType, cancellationToken);
        await EnsureNotDuplicateAsync(dictType, request.DictLabel.Trim(), request.DictValue.Trim(), excludeId: null, cancellationToken);

        var entity = _mapper.Map<SysDictData>(request);
        var inserted = await _dictDataRepository.InsertAsync(entity, cancellationToken);
        await _cache.RemoveAsync(DictRules.DataTypeCacheKey(dictType));
        return inserted.Id;
    }

    /// <summary>校验所属字典类型存在。</summary>
    private async Task EnsureTypeExistsAsync(string dictType, CancellationToken cancellationToken)
    {
        if (await _dictTypeRepository.CountAsync(t => t.DictType == dictType, cancellationToken) == 0)
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
        if (await _dictDataRepository.CountAsync(labelPredicate, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DictDataDuplicate, $"字典类型「{dictType}」下标签「{label}」已存在");
        }

        System.Linq.Expressions.Expression<Func<SysDictData, bool>> valuePredicate = excludeId == null
            ? d => d.DictType == dictType && d.DictValue == value
            : d => d.DictType == dictType && d.DictValue == value && d.Id != excludeId.Value;
        if (await _dictDataRepository.CountAsync(valuePredicate, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DictDataDuplicate, $"字典类型「{dictType}」下键值「{value}」已存在");
        }
    }
}

/// <summary>创建字典类型命令处理器。</summary>
public sealed class CreateDictTypeCommandHandler : CommandHandler<CreateDictTypeCommand, Guid>
{
    private readonly IAggregateRepository<SysDictType> _dictTypeRepository;

    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="dictTypeRepository">字典类型仓储。</param>
    /// <param name="mapper">DTO → 实体映射。</param>
    public CreateDictTypeCommandHandler(IAggregateRepository<SysDictType> dictTypeRepository, IMapper mapper)
    {
        _dictTypeRepository = dictTypeRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateDictTypeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var dictType = request.DictType.Trim();
        if (await _dictTypeRepository.CountAsync(t => t.DictType == dictType, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DictTypeDuplicate, $"字典类型「{dictType}」已存在");
        }

        var entity = _mapper.Map<SysDictType>(request);
        var inserted = await _dictTypeRepository.InsertAsync(entity, cancellationToken);
        return inserted.Id;
    }
}

/// <summary>删除字典数据命令处理器。</summary>
public sealed class DeleteDictDataCommandHandler : CommandHandler<DeleteDictDataCommand>
{
    private readonly IAggregateRepository<SysDictData> _dictDataRepository;
    private readonly ICache _cache;

    /// <summary>构造函数。</summary>
    /// <param name="dictDataRepository">字典数据仓储。</param>
    /// <param name="cache">内存缓存（删除后失效对应类型缓存）。</param>
    public DeleteDictDataCommandHandler(IAggregateRepository<SysDictData> dictDataRepository, ICache cache)
    {
        _dictDataRepository = dictDataRepository;
        _cache = cache;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteDictDataCommand command, CancellationToken cancellationToken)
    {
        var entity = await _dictDataRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DictDataNotFound, $"字典数据 {command.Id} 不存在");

        await _dictDataRepository.DeleteAsync(entity, cancellationToken);
        await _cache.RemoveAsync(DictRules.DataTypeCacheKey(entity.DictType));
    }
}

/// <summary>删除字典类型命令处理器。</summary>
public sealed class DeleteDictTypeCommandHandler : CommandHandler<DeleteDictTypeCommand>
{
    private readonly IAggregateRepository<SysDictType> _dictTypeRepository;
    private readonly IAggregateRepository<SysDictData> _dictDataRepository;

    /// <summary>构造函数。</summary>
    /// <param name="dictTypeRepository">字典类型仓储。</param>
    /// <param name="dictDataRepository">字典数据仓储（删除前置校验）。</param>
    public DeleteDictTypeCommandHandler(
        IAggregateRepository<SysDictType> dictTypeRepository,
        IAggregateRepository<SysDictData> dictDataRepository)
    {
        _dictTypeRepository = dictTypeRepository;
        _dictDataRepository = dictDataRepository;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteDictTypeCommand command, CancellationToken cancellationToken)
    {
        var entity = await _dictTypeRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DictTypeNotFound, $"字典类型 {command.Id} 不存在");

        if (await _dictDataRepository.CountAsync(d => d.DictType == entity.DictType, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DictTypeHasData, "该字典类型下存在字典数据，不允许删除");
        }

        await _dictTypeRepository.DeleteAsync(entity, cancellationToken);
    }
}

/// <summary>更新字典数据命令处理器。</summary>
public sealed class UpdateDictDataCommandHandler : CommandHandler<UpdateDictDataCommand>
{
    private readonly IAggregateRepository<SysDictData> _dictDataRepository;
    private readonly IAggregateRepository<SysDictType> _dictTypeRepository;
    private readonly ICache _cache;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="dictDataRepository">字典数据仓储。</param>
    /// <param name="dictTypeRepository">字典类型仓储（类型迁移时存在性校验）。</param>
    /// <param name="cache">内存缓存（写后失效新旧类型缓存）。</param>
    /// <param name="mapper">DTO → 实体映射。</param>
    public UpdateDictDataCommandHandler(
        IAggregateRepository<SysDictData> dictDataRepository,
        IAggregateRepository<SysDictType> dictTypeRepository,
        ICache cache,
        IMapper mapper)
    {
        _dictDataRepository = dictDataRepository;
        _dictTypeRepository = dictTypeRepository;
        _cache = cache;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateDictDataCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = await _dictDataRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DictDataNotFound, $"字典数据 {command.Id} 不存在");

        var dictType = request.DictType.Trim();
        if (!string.Equals(dictType, entity.DictType, StringComparison.Ordinal))
        {
            if (await _dictTypeRepository.CountAsync(t => t.DictType == dictType, cancellationToken) == 0)
            {
                throw new BusinessException(ErrorCodes.DictTypeNotFound, $"字典类型「{dictType}」不存在");
            }
        }

        await EnsureNotDuplicateAsync(dictType, request.DictLabel.Trim(), request.DictValue.Trim(), excludeId: command.Id, cancellationToken);

        var originalType = entity.DictType;
        _mapper.Map(request, entity);
        await _dictDataRepository.UpdateAsync(entity, cancellationToken);

        // 类型迁移时新旧类型缓存均失效，保证两个维度都立即读到最新数据
        await _cache.RemoveAsync(DictRules.DataTypeCacheKey(dictType));
        if (!string.Equals(originalType, dictType, StringComparison.Ordinal))
        {
            await _cache.RemoveAsync(DictRules.DataTypeCacheKey(originalType));
        }
    }

    /// <summary>校验同字典类型下标签 / 键值未被占用（更新时排除自身）。</summary>
    private async Task EnsureNotDuplicateAsync(string dictType, string label, string value, Guid? excludeId, CancellationToken cancellationToken)
    {
        System.Linq.Expressions.Expression<Func<SysDictData, bool>> labelPredicate = excludeId == null
            ? d => d.DictType == dictType && d.DictLabel == label
            : d => d.DictType == dictType && d.DictLabel == label && d.Id != excludeId.Value;
        if (await _dictDataRepository.CountAsync(labelPredicate, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DictDataDuplicate, $"字典类型「{dictType}」下标签「{label}」已存在");
        }

        System.Linq.Expressions.Expression<Func<SysDictData, bool>> valuePredicate = excludeId == null
            ? d => d.DictType == dictType && d.DictValue == value
            : d => d.DictType == dictType && d.DictValue == value && d.Id != excludeId.Value;
        if (await _dictDataRepository.CountAsync(valuePredicate, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.DictDataDuplicate, $"字典类型「{dictType}」下键值「{value}」已存在");
        }
    }
}

/// <summary>更新字典类型命令处理器。</summary>
public sealed class UpdateDictTypeCommandHandler : CommandHandler<UpdateDictTypeCommand>
{
    private readonly IAggregateRepository<SysDictType> _dictTypeRepository;

    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="dictTypeRepository">字典类型仓储。</param>
    /// <param name="mapper">DTO → 实体映射。</param>
    public UpdateDictTypeCommandHandler(IAggregateRepository<SysDictType> dictTypeRepository, IMapper mapper)
    {
        _dictTypeRepository = dictTypeRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateDictTypeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = await _dictTypeRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.DictTypeNotFound, $"字典类型 {command.Id} 不存在");

        var dictType = request.DictType.Trim();
        if (!string.Equals(dictType, entity.DictType, StringComparison.Ordinal))
        {
            throw new BusinessException(ErrorCodes.DictTypeImmutable, "字典类型（dictType）不可变更");
        }

        _mapper.Map(request, entity);
        await _dictTypeRepository.UpdateAsync(entity, cancellationToken);
    }
}
