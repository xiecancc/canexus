using Application.Contracts.Abstractions;
using Application.Contracts.Online;
using Domain.Shared.Paging;

namespace Application.Contracts.Queries;
/// <summary>分页查询在线会话（账号 / IP 过滤；一条活跃 refreshToken 记录代表一个在线会话）。</summary>
public sealed record OnlineUserQueryPaged : AggregateQueryPaged<OnlineUserPageRequest, OnlineUserPagedResponse>
{
    /// <summary>构造在线会话分页查询。</summary>
    /// <param name="request">分页与筛选条件。</param>
    public OnlineUserQueryPaged(OnlineUserPageRequest request) : base(request) { }
}
