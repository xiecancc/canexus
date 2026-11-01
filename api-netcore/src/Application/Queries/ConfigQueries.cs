
using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Configs;
using Application.Contracts.Queries;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Rules;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.Shared.Caching;

namespace Application.Queries;
/// <summary>按参数键名查询参数值处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="configRepository">参数配置仓储（只读）。</param>
/// <param name="cache">内存缓存。</param>
public sealed class ConfigQueryByKeyHandler(IDomainRepository<SysConfig> configRepository, ICache cache) : QueryHandler<ConfigQueryByKey, string>
{
    /// <inheritdoc />
    protected override async Task<string> HandleAsync(ConfigQueryByKey query, CancellationToken cancellationToken)
    {
        var key = query.ConfigKey.Trim();
        var cacheKey = ConfigRules.ValueCacheKey(key);

        // 常规档：L1 1 分钟 / L2 10 分钟；写操作后按键失效（见 ConfigCommands）
        return await cache.GetOrCreateAsync(cacheKey, async token =>
        {
            var config = (await configRepository.GetListAsync([c => c.ConfigKey == key], cancellationToken: token)).FirstOrDefault()
                ?? throw new BusinessException(ErrorCodes.ConfigNotFound, $"参数「{key}」不存在");
            return config.ConfigValue;
        }, CacheTtl.Medium, cancellationToken);
    }
}

/// <summary>分页查询参数配置处理器（标准分页流程：关键字模糊匹配名称 / 键名 / 键值）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="configRepository">参数配置仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class ConfigQueryPagedHandler(IDomainRepository<SysConfig> configRepository, IMapper mapper) : DomainQueryPagedHandler<ConfigQueryPaged, ConfigPageRequest, SysConfig, ConfigListResponse>(configRepository, mapper)
{
    /// <inheritdoc />
    protected override Expression<Func<SysConfig, bool>>? BuildPredicate(ConfigPageRequest request)
    {
        var keyword = NormalizeFilter(request.Keyword);
        return c => keyword == null || c.ConfigName.Contains(keyword) || c.ConfigKey.Contains(keyword) || c.ConfigValue.Contains(keyword);
    }
}
