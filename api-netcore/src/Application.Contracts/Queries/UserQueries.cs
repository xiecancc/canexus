
using Application.Contracts.Abstractions;
using Application.Contracts.Users;
using Domain.Shared.Paging;

namespace Application.Contracts.Queries;
/// <summary>获取用户详情（含角色 Id 集合）。</summary>
/// <param name="Id">用户 Id。</param>
public sealed record GetUserDetailQuery(Guid Id) : IQuery<UserDetailVO>;

/// <summary>分页查询用户（keyword 匹配用户名 / 昵称 / 手机号；deptId 按部门及以下；叠加数据范围过滤）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record GetUserPageQuery(UserPageRequest Request) : IQuery<PagedResult<UserListVO>>;
