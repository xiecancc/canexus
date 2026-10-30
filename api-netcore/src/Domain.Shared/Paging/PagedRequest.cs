namespace Domain.Shared.Paging;

/// <summary>分页查询请求（页索引从 1 开始，页大小越界自动收敛）。</summary>
public class PagedRequest
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 500;

    private int _pageIndex = 1;
    private int _pageSize = DefaultPageSize;

    /// <summary>页索引，从 1 开始；小于 1 时自动修正为 1。</summary>
    public int PageIndex
    {
        get => _pageIndex;
        set => _pageIndex = value < 1 ? 1 : value;
    }

    /// <summary>每页条数；小于 1 时回退默认值 10，超过上限 500 时收敛为 500（防止恶意大页查询）。</summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1
            ? DefaultPageSize
            : value > MaxPageSize ? MaxPageSize : value;
    }
}
