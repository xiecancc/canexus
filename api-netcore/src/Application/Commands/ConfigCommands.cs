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
/// <remarks>构造函数。</remarks>
/// <param name="configRepository">参数配置仓储。</param>
/// <param name="cache">内存缓存（写后按 key 失效）。</param>
public sealed class CreateConfigCommandHandler(IAggregateRepository<SysConfig> configRepository, ICache cache, IMapper mapper) : CommandHandler<CreateConfigCommand, Guid>
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
        await cache.RemoveAsync(ConfigRules.ValueCacheKey(configKey));
        return inserted.Id;
    }
}

/// <summary>删除参数配置命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="configRepository">参数配置仓储。</param>
/// <param name="cache">内存缓存（写后按 key 失效）。</param>
public sealed class DeleteConfigCommandHandler(IAggregateRepository<SysConfig> configRepository, ICache cache) : CommandHandler<DeleteConfigCommand>
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

        await configRepository.DeleteAsync(entity, cancellationToken);
        await cache.RemoveAsync(ConfigRules.ValueCacheKey(entity.ConfigKey));
    }
}

/// <summary>更新参数配置命令处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="configRepository">参数配置仓储。</param>
/// <param name="cache">内存缓存（写后按 key 失效）。</param>
/// <param name="mapper">DTO → 实体映射。</param>
public sealed class UpdateConfigCommandHandler(IAggregateRepository<SysConfig> configRepository, ICache cache, IMapper mapper) : CommandHandler<UpdateConfigCommand>
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

        var originalKey = entity.ConfigKey;
        mapper.Map(request, entity);
        await configRepository.UpdateAsync(entity, cancellationToken);

        // 键名变更时新旧键缓存均失效，保证两个维度都立即读到最新值
        await cache.RemoveAsync(ConfigRules.ValueCacheKey(configKey));
        if (!string.Equals(originalKey, configKey, StringComparison.Ordinal))
        {
            await cache.RemoveAsync(ConfigRules.ValueCacheKey(originalKey));
        }
    }
}
