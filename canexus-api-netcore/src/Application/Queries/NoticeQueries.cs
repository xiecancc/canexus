
using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Notices;
using Application.Contracts.Queries;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;
using Domain.Shared.Paging;
using Domain.Shared.Results;

namespace Application.Queries;
/// <summary>查询最新通知公告处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="noticeRepository">通知公告仓储。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class NoticeQueryLatestHandler(IAggregateRepository<SysNotice> noticeRepository, IMapper mapper) : QueryHandler<NoticeQueryLatest, List<NoticeLatestResponse>>
{
    /// <inheritdoc />
    protected override async Task<List<NoticeLatestResponse>> HandleAsync(NoticeQueryLatest query, CancellationToken cancellationToken)
    {
        var limit = NoticeQueryLimits.NormalizeLatestCount(query.Count);
        // 排序与取数下推到数据库：复用通用分页查询，PageIndex=1 / PageSize=limit 即 SQL 层 ORDER BY + LIMIT，
        // 替代原「拉全表 → 内存排序 → Take」。orders 与原内存规则一致（创建时间倒序、Id 倒序作平局裁决），
        // 过滤条件与软删除 / 租户全局过滤器语义保持不变。
        var paged = await noticeRepository.GetPagedAsync(
            new PagedRequest { PageIndex = 1, PageSize = limit },
            [n => n.Status == CommonStatus.Enabled],
            new Dictionary<Expression<Func<SysNotice, object>>, bool?>
            {
                [n => n.CreatedAt] = false,
                [n => n.Id] = false
            },
            cancellationToken);
        return mapper.Map<List<NoticeLatestResponse>>(paged.Items);
    }
}

/// <summary>查询通知公告详情处理器（标准详情流程；正文经详情映射直通，无需回填钩子）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="noticeRepository">通知公告仓储。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class NoticeQueryDetailHandler(IAggregateRepository<SysNotice> noticeRepository, IMapper mapper) : AggregateQueryDetailHandler<NoticeQueryDetail, SysNotice, NoticeDetailResponse>(noticeRepository, mapper)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.NoticeNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "通知公告";
}

/// <summary>分页查询通知公告处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="noticeRepository">通知公告仓储。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class NoticeQueryPagedHandler(IAggregateRepository<SysNotice> noticeRepository, IMapper mapper) : QueryHandler<NoticeQueryPaged, PagedResponse<NoticePagedResponse>>
{
    /// <inheritdoc />
    protected override async Task<PagedResponse<NoticePagedResponse>> HandleAsync(NoticeQueryPaged query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var title = NormalizeKeyword(request.Title);
        var noticeType = request.NoticeType;
        var status = request.Status;

        Expression<Func<SysNotice, bool>> predicate = n =>
            (title == null || n.Title.Contains(title))
            && (noticeType == null || n.NoticeType == noticeType.Value)
            && (status == null || n.Status == status.Value);

        // DB 端排序分页：创建时间倒序，Id 倒序作平局裁决（替代原全量拉取后内存排序分页）
        var paged = await noticeRepository.GetPagedAsync(
            request,
            [predicate],
            new Dictionary<Expression<Func<SysNotice, object>>, bool?>
            {
                [n => n.CreatedAt] = false,
                [n => n.Id] = false
            },
            cancellationToken);

        // 页内实体列表整体集合映射（元素映射运行期按模块注册解析；列表映射忽略正文）
        return PagedResponse<NoticePagedResponse>.Create(
            mapper.Map<List<NoticePagedResponse>>(paged.Items), paged.Total, request);
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
