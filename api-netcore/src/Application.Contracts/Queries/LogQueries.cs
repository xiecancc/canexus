
using Application.Contracts.Abstractions;
using Application.Contracts.Logs;
using Domain.Shared.Paging;

namespace Application.Contracts.Queries;
/// <summary>分页查询登录日志（账号 / IP / 状态 / 时间范围）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record GetLoginLogPageQuery(LoginLogPageRequest Request) : IQuery<PagedResult<LoginLogListVO>>;

/// <summary>分页查询操作日志（模块 / 业务类型 / 状态 / 操作人 / 时间范围）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record GetOperLogPageQuery(OperLogPageRequest Request) : IQuery<PagedResult<OperLogListVO>>;
