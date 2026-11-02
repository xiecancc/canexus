
using Application.Contracts.Abstractions;
using Application.Contracts.Logs;
using Domain.Shared.Paging;

namespace Application.Contracts.Queries;
/// <summary>分页查询登录日志（账号 / IP / 状态 / 时间范围）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record LoginLogQueryPaged : AggregateQueryPaged<LoginLogPageRequest, LoginLogPagedResponse>
{
    /// <summary>构造登录日志分页查询。</summary>
    /// <param name="request">分页与筛选条件。</param>
    public LoginLogQueryPaged(LoginLogPageRequest request) : base(request) { }
}

/// <summary>分页查询操作日志（模块 / 业务类型 / 状态 / 操作人 / 时间范围）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record OperLogQueryPaged : AggregateQueryPaged<OperLogPageRequest, OperLogPagedResponse>
{
    public OperLogQueryPaged(OperLogPageRequest request) : base(request) { }
}
