
using Application.Contracts.Abstractions;
using Application.Contracts.Dicts;
using Domain.Shared.Paging;

namespace Application.Contracts.Queries;
/// <summary>按字典类型查询启用状态的字典数据（走缓存，写操作后失效）。</summary>
/// <param name="DictType">字典类型。</param>
public sealed record DictDataQueryByType : AggregateQueryList<DictDataByTypeRequest, DictDataDropdownResponse>
{
    /// <summary>构造按类型查询请求。</summary>
    /// <param name="dictType">字典类型编码。</param>
    public DictDataQueryByType(string dictType) : base(new DictDataByTypeRequest(dictType)) { }
}

/// <summary>分页查询字典数据（dictType 精确、keyword 模糊匹配标签 / 键值）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record DictDataQueryPaged : DomainQueryPaged<DictDataPageRequest, DictDataListResponse>
{
    public DictDataQueryPaged(DictDataPageRequest request) : base(request) { }
}

/// <summary>分页查询字典类型（keyword 模糊匹配字典名称 / 字典类型）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record DictTypeQueryPaged : DomainQueryPaged<DictTypePageRequest, DictTypeListResponse>
{
    public DictTypeQueryPaged(DictTypePageRequest request) : base(request) { }
}
