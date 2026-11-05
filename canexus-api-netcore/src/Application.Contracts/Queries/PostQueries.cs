using Application.Contracts.Abstractions;
using Application.Contracts.Posts;

namespace Application.Contracts.Queries;
/// <summary>按 Id 查询岗位详情（供编辑回显与查看）。</summary>
public sealed record PostQueryDetail : AggregateQueryDetail<PostDetailResponse>
{
    /// <summary>构造岗位详情查询。</summary>
    /// <param name="id">岗位 Id。</param>
    public PostQueryDetail(Guid id) : base(id) { }
}

/// <summary>分页查询岗位（名称 / 编码模糊、状态精确，按显示顺序升序、创建时间倒序）。</summary>
public sealed record PostQueryPaged : AggregateQueryPaged<PostPageRequest, PostPagedResponse>
{
    /// <summary>构造岗位分页查询。</summary>
    /// <param name="request">分页与筛选条件。</param>
    public PostQueryPaged(PostPageRequest request) : base(request) { }
}

/// <summary>岗位下拉查询（用户管理页岗位分配数据源；仅启用岗位，按显示顺序升序）。</summary>
public sealed record PostQueryDropdown() : AggregateQueryDropdown<PostDropdownRequest, PostDropdownResponse>(new PostDropdownRequest());
