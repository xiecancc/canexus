
using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Dicts;
using Application.Contracts.Queries;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Rules;
using Domain.Shared.Enums;
using Domain.Shared.Paging;
using Infrastructure.Shared.Caching;

namespace Application.Queries;
/// <summary>按字典类型查询字典数据处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="dictDataRepository">字典数据仓储。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
/// <param name="cache">内存缓存（写操作后按类型失效）。</param>
public sealed class GetDictDataByTypeQueryHandler(IAggregateRepository<SysDictData> dictDataRepository, IMapper mapper, ICache cache) : QueryHandler<GetDictDataByTypeQuery, List<DictDataSimpleVO>>
{
    /// <inheritdoc />
    protected override async Task<List<DictDataSimpleVO>> HandleAsync(GetDictDataByTypeQuery query, CancellationToken cancellationToken)
    {
        var type = query.DictType.Trim();
        var cacheKey = DictRules.DataTypeCacheKey(type);

        // 常规档：L1 1 分钟 / L2 10 分钟；字典写操作后按类型失效（见 DictCommands）
        return await cache.GetOrCreateAsync(cacheKey, async token =>
        {
            return (await dictDataRepository.GetListAsync([d => d.DictType == type && d.Status == CommonStatus.Enabled], cancellationToken: token))
                .OrderBy(d => d.OrderNum)
                .ThenBy(d => d.Id)
                .Select(d => mapper.Map<DictDataSimpleVO>(d))
                .ToList();
        }, CacheTtl.Medium, cancellationToken);
    }
}

/// <summary>分页查询字典数据处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="dictDataRepository">字典数据仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class GetDictDataPageQueryHandler(IDomainRepository<SysDictData> dictDataRepository, IMapper mapper) : QueryHandler<GetDictDataPageQuery, PagedResult<DictDataListVO>>
{
    /// <inheritdoc />
    protected override async Task<PagedResult<DictDataListVO>> HandleAsync(GetDictDataPageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var dictType = string.IsNullOrWhiteSpace(request.DictType) ? null : request.DictType.Trim();
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        Expression<Func<SysDictData, bool>> predicate = d =>
            (dictType == null || d.DictType == dictType)
            && (keyword == null || d.DictLabel.Contains(keyword) || d.DictValue.Contains(keyword));

        var page = await dictDataRepository.GetPagedAsync(request, [predicate], cancellationToken: cancellationToken);
        var items = page.Items.Select(d => mapper.Map<DictDataListVO>(d)).ToList();
        return PagedResult<DictDataListVO>.Create(items, page.Total, request);
    }
}

/// <summary>分页查询字典类型处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="dictTypeRepository">字典类型仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class GetDictTypePageQueryHandler(IDomainRepository<SysDictType> dictTypeRepository, IMapper mapper) : QueryHandler<GetDictTypePageQuery, PagedResult<DictTypeListVO>>
{
    /// <inheritdoc />
    protected override async Task<PagedResult<DictTypeListVO>> HandleAsync(GetDictTypePageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        Expression<Func<SysDictType, bool>> predicate = t =>
            keyword == null || t.DictName.Contains(keyword) || t.DictType.Contains(keyword);

        var page = await dictTypeRepository.GetPagedAsync(request, [predicate], cancellationToken: cancellationToken);
        var items = page.Items.Select(t => mapper.Map<DictTypeListVO>(t)).ToList();
        return PagedResult<DictTypeListVO>.Create(items, page.Total, request);
    }
}
