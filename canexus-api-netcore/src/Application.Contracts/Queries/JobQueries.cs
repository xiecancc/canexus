
using Application.Contracts.Abstractions;
using Application.Contracts.Jobs;
using Domain.Shared.Paging;

namespace Application.Contracts.Queries;
/// <summary>分页查询任务执行日志（jobId / status 精确过滤，按触发时间倒序，最新在前）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record JobLogQueryPaged : AggregateQueryPaged<JobLogPageRequest, JobLogListResponse>
{
    /// <summary>构造任务日志分页查询。</summary>
    /// <param name="request">分页与筛选条件。</param>
    public JobLogQueryPaged(JobLogPageRequest request) : base(request) { }
}

/// <summary>分页查询定时任务（keyword 模糊匹配任务名称 / 任务分组；status 精确）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record JobQueryPaged : AggregateQueryPaged<JobPageRequest, JobListResponse>
{
    public JobQueryPaged(JobPageRequest request) : base(request) { }
}
