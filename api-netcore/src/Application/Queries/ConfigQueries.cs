
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
using Domain.Shared.Paging;
using Domain.Shared.Results;
using Infrastructure.Shared.Caching;

namespace Application.Queries;
/// <summary>按参数键名查询参数值处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="configRepository">参数配置仓储（只读）。</param>
/// <param name="cache">内存缓存。</param>
public sealed class GetConfigByKeyQueryHandler(IDomainRepository<SysConfig> configRepository, ICache cache) : QueryHandler<GetConfigByKeyQuery, string>
{
    /// <inheritdoc />
    protected override async Task<string> HandleAsync(GetConfigByKeyQuery query, CancellationToken cancellationToken)
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

/// <summary>分页查询参数配置处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="configRepository">参数配置仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class GetConfigPageQueryHandler(IDomainRepository<SysConfig> configRepository, IMapper mapper) : QueryHandler<GetConfigPageQuery, PagedResult<ConfigListVO>>
{
    /// <inheritdoc />
    protected override async Task<PagedResult<ConfigListVO>> HandleAsync(GetConfigPageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        Expression<Func<SysConfig, bool>> predicate = c =>
            keyword == null || c.ConfigName.Contains(keyword) || c.ConfigKey.Contains(keyword) || c.ConfigValue.Contains(keyword);

        var page = await configRepository.GetPagedAsync(request.PageIndex, request.PageSize, [predicate], cancellationToken: cancellationToken);
        var items = page.Items.Select(c => mapper.Map<ConfigListVO>(c)).ToList();
        return PagedResult<ConfigListVO>.Create(items, page.Total, request);
    }
}
