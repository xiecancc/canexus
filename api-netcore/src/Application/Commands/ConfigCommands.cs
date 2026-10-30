namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Configs;
using Application.Mappings;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Rules;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.Shared.Caching;

/// <summary>创建参数配置命令处理器。</summary>
public sealed class CreateConfigCommandHandler : CommandHandler<CreateConfigCommand, Guid>
{
    private readonly IAggregateRepository<SysConfig> _configRepository;
    private readonly ICache _cache;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="configRepository">参数配置仓储。</param>
    /// <param name="cache">内存缓存（写后按 key 失效）。</param>
    public CreateConfigCommandHandler(IAggregateRepository<SysConfig> configRepository, ICache cache, IMapper mapper)
    {
        _configRepository = configRepository;
        _cache = cache;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateConfigCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var configKey = request.ConfigKey.Trim();
        if (await _configRepository.CountAsync(c => c.ConfigKey == configKey, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.ConfigKeyDuplicate, $"参数键名「{configKey}」已存在");
        }

        var entity = _mapper.Map<SysConfig>(request);
        var inserted = await _configRepository.InsertAsync(entity, cancellationToken);
        await _cache.RemoveAsync(ConfigRules.ValueCacheKey(configKey));
        return inserted.Id;
    }
}

/// <summary>删除参数配置命令处理器。</summary>
public sealed class DeleteConfigCommandHandler : CommandHandler<DeleteConfigCommand>
{
    private readonly IAggregateRepository<SysConfig> _configRepository;
    private readonly ICache _cache;

    /// <summary>构造函数。</summary>
    /// <param name="configRepository">参数配置仓储。</param>
    /// <param name="cache">内存缓存（写后按 key 失效）。</param>
    public DeleteConfigCommandHandler(IAggregateRepository<SysConfig> configRepository, ICache cache)
    {
        _configRepository = configRepository;
        _cache = cache;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteConfigCommand command, CancellationToken cancellationToken)
    {
        var entity = await _configRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.ConfigNotFound, $"参数配置 {command.Id} 不存在");

        if (entity.BuiltIn)
        {
            throw new BusinessException(ErrorCodes.ConfigBuiltInProtected, "内置参数不允许删除");
        }

        await _configRepository.DeleteAsync(entity, cancellationToken);
        await _cache.RemoveAsync(ConfigRules.ValueCacheKey(entity.ConfigKey));
    }
}

/// <summary>更新参数配置命令处理器。</summary>
public sealed class UpdateConfigCommandHandler : CommandHandler<UpdateConfigCommand>
{
    private readonly IAggregateRepository<SysConfig> _configRepository;
    private readonly ICache _cache;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="configRepository">参数配置仓储。</param>
    /// <param name="cache">内存缓存（写后按 key 失效）。</param>
    /// <param name="mapper">DTO → 实体映射。</param>
    public UpdateConfigCommandHandler(IAggregateRepository<SysConfig> configRepository, ICache cache, IMapper mapper)
    {
        _configRepository = configRepository;
        _cache = cache;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateConfigCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = await _configRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.ConfigNotFound, $"参数配置 {command.Id} 不存在");

        var configKey = request.ConfigKey.Trim();
        if (await _configRepository.CountAsync(c => c.ConfigKey == configKey && c.Id != command.Id, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.ConfigKeyDuplicate, $"参数键名「{configKey}」已存在");
        }

        var originalKey = entity.ConfigKey;
        _mapper.Map(request, entity);
        await _configRepository.UpdateAsync(entity, cancellationToken);

        // 键名变更时新旧键缓存均失效，保证两个维度都立即读到最新值
        await _cache.RemoveAsync(ConfigRules.ValueCacheKey(configKey));
        if (!string.Equals(originalKey, configKey, StringComparison.Ordinal))
        {
            await _cache.RemoveAsync(ConfigRules.ValueCacheKey(originalKey));
        }
    }
}
