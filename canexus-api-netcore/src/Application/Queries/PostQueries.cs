
using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Posts;
using Application.Contracts.Queries;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;
using Domain.Shared.Paging;
using Domain.Shared.Results;

namespace Application.Queries;
/// <summary>查询岗位详情处理器（标准详情流程）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class PostQueryDetailHandler(IAggregateRepository<SysPost> postRepository, IMapper mapper)
    : AggregateQueryDetailHandler<PostQueryDetail, SysPost, PostDetailResponse>(postRepository, mapper)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.PostNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "岗位";
}

/// <summary>分页查询岗位处理器（名称 / 编码模糊、状态精确；显示顺序升序、创建时间倒序、Id 倒序平局裁决）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class PostQueryPagedHandler(IAggregateRepository<SysPost> postRepository, IMapper mapper)
    : QueryHandler<PostQueryPaged, PagedResponse<PostPagedResponse>>
{
    /// <inheritdoc />
    protected override async Task<PagedResponse<PostPagedResponse>> HandleAsync(PostQueryPaged query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var postName = NormalizeKeyword(request.PostName);
        var postCode = NormalizeKeyword(request.PostCode);
        var status = request.Status;

        Expression<Func<SysPost, bool>> predicate = p =>
            (postName == null || p.PostName.Contains(postName))
            && (postCode == null || p.PostCode.Contains(postCode))
            && (status == null || p.Status == status.Value);

        var paged = await postRepository.GetPagedAsync(
            request,
            [predicate],
            new Dictionary<Expression<Func<SysPost, object>>, bool?>
            {
                [p => p.OrderNum] = true,
                [p => p.CreatedAt] = false,
                [p => p.Id] = false
            },
            cancellationToken);

        return PagedResponse<PostPagedResponse>.Create(
            mapper.Map<List<PostPagedResponse>>(paged.Items), paged.Total, request);
    }
}

/// <summary>岗位下拉查询处理器（仅启用岗位，显示顺序升序）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class PostQueryDropdownHandler(IAggregateRepository<SysPost> postRepository, IMapper mapper)
    : AggregateQueryDropdownHandler<PostQueryDropdown, PostDropdownRequest, SysPost, PostDropdownResponse>(postRepository, mapper)
{
    /// <inheritdoc />
    protected override Expression<Func<SysPost, bool>>? BuildDropdownPredicate() => p => p.Status == CommonStatus.Enabled;

    /// <inheritdoc />
    protected override IDictionary<Expression<Func<SysPost, object>>, bool?>? BuildDropdownOrders() =>
        new Dictionary<Expression<Func<SysPost, object>>, bool?>
        {
            [p => p.OrderNum] = true,
            [p => p.CreatedAt] = false
        };
}
