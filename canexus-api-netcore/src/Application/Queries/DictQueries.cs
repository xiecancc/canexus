
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
using Infrastructure.Shared.Caching;

namespace Application.Queries;
/// <summary>按字典类型查询字典数据处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="dictDataRepository">字典数据仓储。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
/// <param name="cache">内存缓存（写操作后按类型失效）。</param>
public sealed class DictDataQueryByTypeHandler(IAggregateRepository<SysDictData> dictDataRepository, IMapper mapper, ICache cache) : QueryHandler<DictDataQueryByType, List<DictDataDropdownResponse>>
{
    /// <inheritdoc />
    protected override async Task<List<DictDataDropdownResponse>> HandleAsync(DictDataQueryByType query, CancellationToken cancellationToken)
    {
        var type = query.Request.DictType.Trim();
        var cacheKey = DictRules.DataTypeCacheKey(type);

        // 常规档：L1 1 分钟 / L2 10 分钟；字典写操作后按类型失效（见 DictCommands）
        return await cache.GetOrCreateAsync(cacheKey, async token =>
        {
            var enabled = (await dictDataRepository.GetListAsync([d => d.DictType == type && d.Status == CommonStatus.Enabled], cancellationToken: token))
                .OrderBy(d => d.OrderNum)
                .ThenBy(d => d.Id)
                .ToList();
            return mapper.Map<List<DictDataDropdownResponse>>(enabled);
        }, CacheTtl.Medium, cancellationToken);
    }
}

/// <summary>分页查询字典数据处理器（标准分页流程：dictType 精确、关键字模糊匹配标签 / 键值）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="dictDataRepository">字典数据仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class DictDataQueryPagedHandler(IAggregateRepository<SysDictData> dictDataRepository, IMapper mapper) : AggregateQueryPagedHandler<DictDataQueryPaged, DictDataPageRequest, SysDictData, DictDataPagedResponse>(dictDataRepository, mapper)
{
    /// <inheritdoc />
    protected override Expression<Func<SysDictData, bool>>? BuildPredicate(DictDataPageRequest request)
    {
        var dictType = NormalizeFilter(request.DictType);
        var keyword = NormalizeFilter(request.Keyword);
        return d =>
            (dictType == null || d.DictType == dictType)
            && (keyword == null || d.DictLabel.Contains(keyword) || d.DictValue.Contains(keyword));
    }
}

/// <summary>分页查询字典类型处理器（标准分页流程：关键字模糊匹配字典名称 / 字典类型）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="dictTypeRepository">字典类型仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class DictTypeQueryPagedHandler(IAggregateRepository<SysDictType> dictTypeRepository, IMapper mapper) : AggregateQueryPagedHandler<DictTypeQueryPaged, DictTypePageRequest, SysDictType, DictTypePagedResponse>(dictTypeRepository, mapper)
{
    /// <inheritdoc />
    protected override Expression<Func<SysDictType, bool>>? BuildPredicate(DictTypePageRequest request)
    {
        var keyword = NormalizeFilter(request.Keyword);
        return t => keyword == null || t.DictName.Contains(keyword) || t.DictType.Contains(keyword);
    }
}
