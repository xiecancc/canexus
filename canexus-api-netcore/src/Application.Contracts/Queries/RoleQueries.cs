
using Application.Contracts.Abstractions;
using Application.Contracts.Roles;

namespace Application.Contracts.Queries;
/// <summary>获取全部启用角色（不分页，含超管角色，用于用户分配角色下拉）。</summary>
public sealed record RoleQueryDropdown : AggregateQueryDropdown<RoleDropdownResponse>;

/// <summary>获取角色详情（含授权菜单 Id 集合，前端用作菜单授权回显）。</summary>
/// <param name="Id">角色 Id。</param>
public sealed record RoleQueryDetail : AggregateQueryDetail<RoleDetailResponse>
{
    public RoleQueryDetail(Guid id) : base(id) { }
}

/// <summary>分页查询角色（keyword 匹配角色名称 / 角色权限字符；status 精确）。</summary>
/// <param name="Request">分页与筛选条件。</param>
public sealed record RoleQueryPaged : DomainQueryPaged<RolePageRequest, RoleListResponse>
{
    public RoleQueryPaged(RolePageRequest request) : base(request) { }
}
