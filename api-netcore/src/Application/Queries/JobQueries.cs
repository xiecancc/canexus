namespace Application.Queries;

using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Jobs;
using Application.Contracts.Queries;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Paging;

/// <summary>分页查询任务执行日志处理器。</summary>
public sealed class GetJobLogPageQueryHandler : QueryHandler<GetJobLogPageQuery, PagedResult<JobLogListVO>>
{
    private readonly IJobLogRepository _jobLogRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="jobLogRepository">任务执行日志仓储（倒序分页）。</param>
    /// <param name="mapper">AutoMapper 映射器。</param>
    public GetJobLogPageQueryHandler(IJobLogRepository jobLogRepository, IMapper mapper)
    {
        _jobLogRepository = jobLogRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<PagedResult<JobLogListVO>> HandleAsync(GetJobLogPageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        Expression<Func<SysJobLog, bool>> predicate = l =>
            (request.JobId == null || l.JobId == request.JobId.Value)
            && (request.Status == null || l.Status == request.Status.Value);

        var page = await _jobLogRepository.GetPagedDescAsync(request, predicate, cancellationToken);
        var items = page.Items.Select(l => _mapper.Map<JobLogListVO>(l)).ToList();
        return PagedResult<JobLogListVO>.Create(items, page.Total, request);
    }
}

/// <summary>分页查询定时任务处理器。</summary>
public sealed class GetJobPageQueryHandler : QueryHandler<GetJobPageQuery, PagedResult<JobListVO>>
{
    private readonly IDomainRepository<SysJob> _jobRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="jobRepository">定时任务仓储（只读）。</param>
    /// <param name="mapper">AutoMapper 映射器。</param>
    public GetJobPageQueryHandler(IDomainRepository<SysJob> jobRepository, IMapper mapper)
    {
        _jobRepository = jobRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<PagedResult<JobListVO>> HandleAsync(GetJobPageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        Expression<Func<SysJob, bool>> predicate = j =>
            (keyword == null || j.JobName.Contains(keyword) || j.JobGroup.Contains(keyword))
            && (request.Status == null || j.Status == request.Status.Value);

        var page = await _jobRepository.GetPagedAsync(request.PageIndex, request.PageSize, [predicate], cancellationToken: cancellationToken);
        var items = page.Items.Select(j => _mapper.Map<JobListVO>(j)).ToList();
        return PagedResult<JobListVO>.Create(items, page.Total, request);
    }
}
