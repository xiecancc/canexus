namespace Domain.Shared.Paging;

/// <summary>分页查询结果。</summary>
/// <typeparam name="T">数据项类型。</typeparam>
public class PagedResult<T>
{
    /// <summary>当前页数据。</summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>总记录数。</summary>
    public long Total { get; init; }

    /// <summary>页索引（从 1 开始）。</summary>
    public int PageIndex { get; init; }

    /// <summary>每页条数。</summary>
    public int PageSize { get; init; }

    /// <summary>创建分页结果。</summary>
    /// <param name="items">当前页数据。</param>
    /// <param name="total">总记录数。</param>
    /// <param name="request">对应的分页请求。</param>
    /// <returns>分页结果。</returns>
    public static PagedResult<T> Create(IEnumerable<T> items, long total, PagedRequest request) => new()
    {
        Items = items.ToList(),
        Total = total,
        PageIndex = request.PageIndex,
        PageSize = request.PageSize
    };

    /// <summary>创建分页结果（直接指定页索引与页大小）。</summary>
    /// <param name="items">当前页数据。</param>
    /// <param name="total">总记录数。</param>
    /// <param name="pageIndex">页索引（从 1 开始）。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <returns>分页结果。</returns>
    public static PagedResult<T> Create(IEnumerable<T> items, long total, int pageIndex, int pageSize) => new()
    {
        Items = items.ToList(),
        Total = total,
        PageIndex = pageIndex,
        PageSize = pageSize
    };
}
