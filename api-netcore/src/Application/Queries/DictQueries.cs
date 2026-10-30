namespace Application.Queries;

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

/// <summary>按字典类型查询字典数据处理器。</summary>
public sealed class GetDictDataByTypeQueryHandler : QueryHandler<GetDictDataByTypeQuery, List<DictDataSimpleVO>>
{
    private readonly IAggregateRepository<SysDictData> _dictDataRepository;
    private readonly IMapper _mapper;
    private readonly ICache _cache;

    /// <summary>构造函数。</summary>
    /// <param name="dictDataRepository">字典数据仓储。</param>
    /// <param name="mapper">AutoMapper 映射器。</param>
    /// <param name="cache">内存缓存（写操作后按类型失效）。</param>
    public GetDictDataByTypeQueryHandler(IAggregateRepository<SysDictData> dictDataRepository, IMapper mapper, ICache cache)
    {
        _dictDataRepository = dictDataRepository;
        _mapper = mapper;
        _cache = cache;
    }

    /// <inheritdoc />
    protected override async Task<List<DictDataSimpleVO>> HandleAsync(GetDictDataByTypeQuery query, CancellationToken cancellationToken)
    {
        var type = query.DictType.Trim();
        var cacheKey = DictRules.DataTypeCacheKey(type);

        // 常规档：L1 1 分钟 / L2 10 分钟；字典写操作后按类型失效（见 DictCommands）
        return await _cache.GetOrCreateAsync(cacheKey, async token =>
        {
            return (await _dictDataRepository.GetListAsync([d => d.DictType == type && d.Status == CommonStatus.Enabled], cancellationToken: token))
                .OrderBy(d => d.OrderNum)
                .ThenBy(d => d.Id)
                .Select(d => _mapper.Map<DictDataSimpleVO>(d))
                .ToList();
        }, CacheTtl.Medium, cancellationToken);
    }
}

/// <summary>分页查询字典数据处理器。</summary>
public sealed class GetDictDataPageQueryHandler : QueryHandler<GetDictDataPageQuery, PagedResult<DictDataListVO>>
{
    private readonly IDomainRepository<SysDictData> _dictDataRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="dictDataRepository">字典数据仓储（只读）。</param>
    /// <param name="mapper">AutoMapper 映射器。</param>
    public GetDictDataPageQueryHandler(IDomainRepository<SysDictData> dictDataRepository, IMapper mapper)
    {
        _dictDataRepository = dictDataRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<PagedResult<DictDataListVO>> HandleAsync(GetDictDataPageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var dictType = string.IsNullOrWhiteSpace(request.DictType) ? null : request.DictType.Trim();
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        Expression<Func<SysDictData, bool>> predicate = d =>
            (dictType == null || d.DictType == dictType)
            && (keyword == null || d.DictLabel.Contains(keyword) || d.DictValue.Contains(keyword));

        var page = await _dictDataRepository.GetPagedAsync(request.PageIndex, request.PageSize, [predicate], cancellationToken: cancellationToken);
        var items = page.Items.Select(d => _mapper.Map<DictDataListVO>(d)).ToList();
        return PagedResult<DictDataListVO>.Create(items, page.Total, request);
    }
}

/// <summary>分页查询字典类型处理器。</summary>
public sealed class GetDictTypePageQueryHandler : QueryHandler<GetDictTypePageQuery, PagedResult<DictTypeListVO>>
{
    private readonly IDomainRepository<SysDictType> _dictTypeRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="dictTypeRepository">字典类型仓储（只读）。</param>
    /// <param name="mapper">AutoMapper 映射器。</param>
    public GetDictTypePageQueryHandler(IDomainRepository<SysDictType> dictTypeRepository, IMapper mapper)
    {
        _dictTypeRepository = dictTypeRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<PagedResult<DictTypeListVO>> HandleAsync(GetDictTypePageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        Expression<Func<SysDictType, bool>> predicate = t =>
            keyword == null || t.DictName.Contains(keyword) || t.DictType.Contains(keyword);

        var page = await _dictTypeRepository.GetPagedAsync(request.PageIndex, request.PageSize, [predicate], cancellationToken: cancellationToken);
        var items = page.Items.Select(t => _mapper.Map<DictTypeListVO>(t)).ToList();
        return PagedResult<DictTypeListVO>.Create(items, page.Total, request);
    }
}
