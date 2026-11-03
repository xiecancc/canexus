
using Application.Contracts.Abstractions;
using Application.Contracts.Users;
using Domain.Shared.Paging;

namespace Application.Contracts.Queries;
/// <summary>获取用户详情（含角色 Id 集合）。</summary>
public sealed record UserQueryDetail : AggregateQueryDetail<UserDetailResponse>
{
    /// <summary>构造用户详情查询。</summary>
    /// <param name="id">用户 Id。</param>
    public UserQueryDetail(Guid id) : base(id) { }
}

/// <summary>分页查询用户（keyword 匹配用户名 / 昵称 / 手机号；deptId 按部门及以下；叠加数据范围过滤）。</summary>
public sealed record UserQueryPaged : AggregateQueryPaged<UserPageRequest, UserPagedResponse>
{
    /// <summary>构造用户分页查询。</summary>
    /// <param name="request">分页与筛选条件。</param>
    public UserQueryPaged(UserPageRequest request) : base(request) { }
}
