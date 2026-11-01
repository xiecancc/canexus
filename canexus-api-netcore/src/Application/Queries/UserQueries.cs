
using System.Linq.Expressions;
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Queries;
using Application.Contracts.Users;
using Application.Permissions;
using Application.Scopes;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Enums;
using Domain.Shared.Paging;
using Domain.Shared.Results;

namespace Application.Queries;
/// <summary>获取用户详情查询处理器（标准详情流程 + 组装已分配角色 Id 集合）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UserQueryDetailHandler(
    IAggregateRepository<SysUser> userRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IMapper mapper) : AggregateQueryDetailHandler<UserQueryDetail, SysUser, UserDetailResponse>(userRepository, mapper)
{
    /// <summary>用户-角色关联仓储（角色 Id 集合组装）。</summary>
    private IDomainRepository<SysUserRole> UserRoleRepository { get; } = userRoleRepository;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.UserNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "用户";

    /// <inheritdoc />
    protected override async Task EnrichAsync(UserDetailResponse response, SysUser entity, CancellationToken cancellationToken)
    {
        response.RoleIds = [.. (await UserRoleRepository.GetListAsync([ur => ur.UserId == entity.Id], cancellationToken: cancellationToken))
            .Select(ur => ur.RoleId)
            .Distinct()];
    }
}

/// <summary>分页查询用户处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UserQueryPagedHandler(
    IAggregateRepository<SysUser> userRepository,
    IAggregateRepository<SysRole> roleRepository,
    IAggregateTreeRepository<SysDept> deptRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IDataScopeFilter dataScopeFilter,
    IUserContext currentUser,
    IMapper mapper) : QueryHandler<UserQueryPaged, PagedResponse<UserListResponse>>
{
    /// <inheritdoc />
    protected override async Task<PagedResponse<UserListResponse>> HandleAsync(UserQueryPaged query, CancellationToken cancellationToken)
    {
        var request = query.Request;

        // 数据范围解析（请求内缓存）；筛选部门子树仅在实际传入 deptId 时查询（含自身 + 全部后代）
        var scope = await dataScopeFilter.GetUserScopeAsync(cancellationToken);
        List<Guid>? deptFilterIds = null;
        if (request.DeptId is not null)
        {
            deptFilterIds = [request.DeptId.Value];
            deptFilterIds.AddRange(
                (await deptRepository.GetDescendantsAsync(request.DeptId.Value, cancellationToken))
                    .Select(d => d.Id));
        }

        var predicate = BuildPredicate(request, scope, deptFilterIds);
        var page = await userRepository.GetPagedAsync(request, [predicate], cancellationToken: cancellationToken);

        // 关联数据一次性批量查询组装（避免 N+1）：部门名 + 角色
        var users = page.Items;
        var userIds = users.Select(u => u.Id).ToList();
        var userRoles = await userRoleRepository.GetListAsync([ur => userIds.Contains(ur.UserId)], cancellationToken: cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();
        var roleMap = (roleIds.Count == 0
                ? []
                : await roleRepository.GetListAsync([r => roleIds.Contains(r.Id)], cancellationToken: cancellationToken))
            .ToDictionary(r => r.Id, r => r);

        var relatedDeptIds = users.Where(u => u.DeptId is not null).Select(u => u.DeptId!.Value).Distinct().ToList();
        var deptMap = (relatedDeptIds.Count == 0
                ? []
                : await deptRepository.GetListAsync([d => relatedDeptIds.Contains(d.Id)], cancellationToken: cancellationToken))
            .ToDictionary(d => d.Id, d => d);

        // 开放泛型映射闭合分页结果，再按实体↔响应顺序配对组装扩展字段（DeptName / Roles）
        var result = mapper.Map<PagedResponse<UserListResponse>>(page)!;
        foreach (var (user, response) in users.Zip(result.Items))
        {
            response.DeptName = user.DeptId is not null && deptMap.TryGetValue(user.DeptId.Value, out var dept)
                ? dept.DeptName
                : null;
            response.Roles = [.. userRoles
                .Where(ur => ur.UserId == user.Id && roleMap.ContainsKey(ur.RoleId))
                .Select(ur => new UserRoleItemResponse
                {
                    Id = ur.RoleId,
                    RoleName = roleMap[ur.RoleId].RoleName
                })];
        }

        return result;
    }

    /// <summary>构造分页查询条件：关键字 / 部门子树 / 状态与数据范围合并为单一表达式。</summary>
    private Expression<Func<SysUser, bool>> BuildPredicate(UserPageRequest request, UserDataScope scope, List<Guid>? deptFilterIds)
    {
        var keyword = NormalizeFilter(request.Keyword);
        var status = request.Status;
        var scopeType = scope.Scope;
        var scopeDeptIds = scope.DeptIds?.ToList();
        var currentUserId = currentUser.UserId ?? Guid.Empty;
        var currentUserDeptId = currentUser.DeptId;

        return u =>
            (keyword == null
                || u.UserName.Contains(keyword)
                || u.NickName.Contains(keyword)
                || (u.Phone != null && u.Phone.Contains(keyword)))
            && (deptFilterIds == null || (u.DeptId != null && deptFilterIds.Contains(u.DeptId.Value)))
            && (status == null || u.Status == status.Value)
            && (scopeType == DataScope.All
                || (scopeType == DataScope.DeptAndBelow && u.DeptId != null && scopeDeptIds!.Contains(u.DeptId.Value))
                || (scopeType == DataScope.Dept && u.DeptId == currentUserDeptId)
                || (scopeType == DataScope.Custom && u.DeptId != null && scopeDeptIds!.Contains(u.DeptId.Value))
                || (scopeType == DataScope.Self && (u.Id == currentUserId || u.CreatedBy == currentUserId)));
    }
}
