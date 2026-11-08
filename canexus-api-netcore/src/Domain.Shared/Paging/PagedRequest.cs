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

    /// <summary>按指定页码 / 页大小复制一份同类型请求（浅拷贝全部筛选条件，不改动原对象）。</summary>
    /// <remarks>导出等场景需以同一筛选条件逐页取数；直接改写共享请求对象会在并发 / 嵌套请求间互相污染，
    /// 且会把导出用的 PageSize 回写到调用方持有的对象上。<c>MemberwiseClone</c> 保留运行时具体类型与全部派生字段。</remarks>
    /// <typeparam name="TRequest">具体请求类型（须继承 <see cref="PagedRequest"/>）。</typeparam>
    /// <param name="pageIndex">目标页索引。</param>
    /// <param name="pageSize">目标页大小。</param>
    /// <returns>筛选条件与原请求一致的新实例。</returns>
    /// <exception cref="InvalidCastException">泛型参数与当前实例的实际类型不一致时抛出。</exception>
    public TRequest CloneForPage<TRequest>(int pageIndex, int pageSize) where TRequest : PagedRequest
    {
        var clone = (TRequest)MemberwiseClone();
        clone.PageIndex = pageIndex;
        clone.PageSize = pageSize;
        return clone;
    }
}
