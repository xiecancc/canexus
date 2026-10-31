
using Application.Contracts.Abstractions;
using Application.Contracts.Dicts;
using Domain.Shared.Paging;

namespace Application.Contracts.Queries;
/// <summary>按字典类型查询启用状态的字典数据（走缓存，写操作后失效）。</summary>
/// <param name="DictType">字典类型。</param>
public sealed record GetDictDataByTypeQuery(string DictType) : IQuery<List<DictDataSimpleVO>>;

/// <summary>分页查询字典数据（dictType 精确、keyword 模糊匹配标签 / 键值）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record GetDictDataPageQuery(DictDataPageRequest Request) : IQuery<PagedResult<DictDataListVO>>;

/// <summary>分页查询字典类型（keyword 模糊匹配字典名称 / 字典类型）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record GetDictTypePageQuery(DictTypePageRequest Request) : IQuery<PagedResult<DictTypeListVO>>;
