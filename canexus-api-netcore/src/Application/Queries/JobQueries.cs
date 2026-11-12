
using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Jobs;
using Application.Contracts.Queries;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Paging;

namespace Application.Queries;
/// <summary>分页查询任务执行日志处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="jobLogRepository">任务执行日志仓储（倒序分页）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class JobLogQueryPagedHandler(IJobLogRepository jobLogRepository, IMapper mapper) : QueryHandler<JobLogQueryPaged, PagedResponse<JobLogPagedResponse>>
{
    /// <inheritdoc />
    protected override async Task<PagedResponse<JobLogPagedResponse>> HandleAsync(JobLogQueryPaged query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        Expression<Func<SysJobLog, bool>> predicate = l =>
            (request.JobId == null || l.JobId == request.JobId.Value)
            && (request.Status == null || l.Status == request.Status.Value);

        var page = await jobLogRepository.GetPagedDescAsync(request, predicate, cancellationToken);

        // 开放泛型映射闭合 PagedResponse<SysJobLog> → PagedResponse<JobLogPagedResponse>：
        // 分页元数据同名复制，Items 元素运行期按模块映射解析
        return mapper.Map<PagedResponse<JobLogPagedResponse>>(page)!;
    }
}

/// <summary>分页查询定时任务处理器（标准分页流程：关键字模糊匹配任务名 / 任务组、状态过滤）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="jobRepository">定时任务仓储（只读）。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class JobQueryPagedHandler(IAggregateRepository<SysJob> jobRepository, IMapper mapper) : AggregateQueryPagedHandler<JobQueryPaged, JobPageRequest, SysJob, JobPagedResponse>(jobRepository, mapper)
{
    /// <inheritdoc />
    protected override Expression<Func<SysJob, bool>>? BuildPredicate(JobPageRequest request)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        return j =>
            (keyword == null || j.JobName.Contains(keyword) || j.JobGroup.Contains(keyword))
            && (request.Status == null || j.Status == request.Status.Value);
    }
}
