namespace Application.Queries;

using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Notices;
using Application.Contracts.Queries;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Paging;
using Domain.Shared.Results;

/// <summary>查询最新通知公告处理器。</summary>
public sealed class GetLatestNoticesQueryHandler : QueryHandler<GetLatestNoticesQuery, List<NoticeLatestVO>>
{
    private readonly IAggregateRepository<SysNotice> _noticeRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="noticeRepository">通知公告仓储。</param>
    /// <param name="mapper">AutoMapper 映射器。</param>
    public GetLatestNoticesQueryHandler(IAggregateRepository<SysNotice> noticeRepository, IMapper mapper)
    {
        _noticeRepository = noticeRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<List<NoticeLatestVO>> HandleAsync(GetLatestNoticesQuery query, CancellationToken cancellationToken)
    {
        var limit = NoticeQueryLimits.NormalizeLatestCount(query.Count);
        var notices = await _noticeRepository.GetListAsync([n => n.Status == CommonStatus.Enabled], cancellationToken: cancellationToken);
        return notices
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(limit)
            .Select(n => _mapper.Map<NoticeLatestVO>(n))
            .ToList();
    }
}

/// <summary>查询通知公告详情处理器。</summary>
public sealed class GetNoticeByIdQueryHandler : QueryHandler<GetNoticeByIdQuery, NoticeListVO>
{
    private readonly IAggregateRepository<SysNotice> _noticeRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="noticeRepository">通知公告仓储。</param>
    /// <param name="mapper">AutoMapper 映射器。</param>
    public GetNoticeByIdQueryHandler(IAggregateRepository<SysNotice> noticeRepository, IMapper mapper)
    {
        _noticeRepository = noticeRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<NoticeListVO> HandleAsync(GetNoticeByIdQuery query, CancellationToken cancellationToken)
    {
        var entity = await _noticeRepository.GetAsync(query.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NoticeNotFound, $"通知公告 {query.Id} 不存在");

        // 列表映射忽略正文，详情在此显式补充
        var vo = _mapper.Map<NoticeListVO>(entity);
        vo.Content = entity.Content;
        return vo;
    }
}

/// <summary>分页查询通知公告处理器。</summary>
public sealed class GetNoticePageQueryHandler : QueryHandler<GetNoticePageQuery, PagedResult<NoticeListVO>>
{
    private readonly IAggregateRepository<SysNotice> _noticeRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    /// <param name="noticeRepository">通知公告仓储。</param>
    /// <param name="mapper">AutoMapper 映射器。</param>
    public GetNoticePageQueryHandler(IAggregateRepository<SysNotice> noticeRepository, IMapper mapper)
    {
        _noticeRepository = noticeRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<PagedResult<NoticeListVO>> HandleAsync(GetNoticePageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        var noticeType = request.NoticeType;
        var status = request.Status;

        Expression<Func<SysNotice, bool>> predicate = n =>
            (title == null || n.Title.Contains(title))
            && (noticeType == null || n.NoticeType == noticeType.Value)
            && (status == null || n.Status == status.Value);

        // 通用仓储分页固定按主键升序；公告要求按创建时间倒序且数据量小，取筛选结果后内存排序分页
        var matched = await _noticeRepository.GetListAsync([predicate], cancellationToken: cancellationToken);
        var ordered = matched
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .ToList();

        var items = ordered
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(n => _mapper.Map<NoticeListVO>(n))
            .ToList();

        return PagedResult<NoticeListVO>.Create(items, ordered.Count, request);
    }

}

/// <summary>最新公告查询数量收敛（原 NoticeRules.NormalizeLatestCount 迁入）。</summary>
internal static class NoticeQueryLimits
{
    /// <summary>缺省条数。</summary>
    public const int LatestDefaultCount = 5;

    /// <summary>上限条数。</summary>
    public const int LatestMaxCount = 20;

    /// <summary>收敛最新公告查询数量：非正数回退缺省值，超过上限收敛为上限。</summary>
    /// <param name="count">请求的返回条数。</param>
    /// <returns>收敛后的条数。</returns>
    public static int NormalizeLatestCount(int count) => count <= 0
        ? LatestDefaultCount
        : Math.Min(count, LatestMaxCount);
}
