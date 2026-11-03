
using Application.Contracts.Abstractions;
using Application.Contracts.Dicts;
using Domain.Shared.Paging;

namespace Application.Contracts.Queries;
/// <summary>按字典类型查询启用状态的字典数据（走缓存，写操作后失效）。</summary>
public sealed record DictDataQueryByType : AggregateQuery<DictDataByTypeRequest, List<DictDataDropdownResponse>>
{
    /// <summary>构造按类型查询请求。</summary>
    /// <param name="dictType">字典类型编码。</param>
    public DictDataQueryByType(string dictType) : base(new DictDataByTypeRequest(dictType)) { }
}

/// <summary>分页查询字典数据（dictType 精确、keyword 模糊匹配标签 / 键值）。</summary>
public sealed record DictDataQueryPaged : AggregateQueryPaged<DictDataPageRequest, DictDataPagedResponse>
{
    /// <summary>构造字典数据分页查询。</summary>
    /// <param name="request">分页与筛选条件。</param>
    public DictDataQueryPaged(DictDataPageRequest request) : base(request) { }
}

/// <summary>分页查询字典类型（keyword 模糊匹配字典名称 / 字典类型）。</summary>
public sealed record DictTypeQueryPaged : AggregateQueryPaged<DictTypePageRequest, DictTypePagedResponse>
{
    /// <summary>构造字典类型分页查询。</summary>
    /// <param name="request">分页与筛选条件。</param>
    public DictTypeQueryPaged(DictTypePageRequest request) : base(request) { }
}
