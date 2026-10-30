namespace Application.Contracts.Queries;

using Application.Contracts.Abstractions;
using Application.Contracts.Jobs;
using Domain.Shared.Paging;

/// <summary>分页查询任务执行日志（jobId / status 精确过滤，按触发时间倒序，最新在前）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record GetJobLogPageQuery(JobLogPageRequest Request) : IQuery<PagedResult<JobLogListVO>>;

/// <summary>分页查询定时任务（keyword 模糊匹配任务名称 / 任务分组；status 精确）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record GetJobPageQuery(JobPageRequest Request) : IQuery<PagedResult<JobListVO>>;
