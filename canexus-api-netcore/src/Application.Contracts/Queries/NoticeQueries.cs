
using Application.Contracts.Abstractions;
using Application.Contracts.Notices;
using Domain.Shared.Paging;

namespace Application.Contracts.Queries;
/// <summary>查询最新通知公告（仅正常状态，按创建时间倒序取 N 条；供首页公告卡片）。</summary>
/// <param name="Count">返回条数（≤ 20，缺省 5）。</param>
public sealed record NoticeQueryLatest(int Count = 5) : Query<List<NoticeLatestResponse>>;

/// <summary>按 Id 查询通知公告详情（含正文，供编辑回显与查看全文）。</summary>
/// <param name="Id">公告 Id。</param>
public sealed record NoticeQueryById : AggregateQueryById<NoticeListResponse>
{
    public NoticeQueryById(Guid id) : base(id) { }
}

/// <summary>分页查询通知公告（title 模糊、类型 / 状态精确，按创建时间倒序；列表不含正文）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record NoticeQueryPaged : DomainQueryPaged<NoticePageRequest, NoticeListResponse>
{
    /// <summary>构造通知公告分页查询。</summary>
    /// <param name="request">分页与筛选条件。</param>
    public NoticeQueryPaged(NoticePageRequest request) : base(request) { }
}
