namespace Application.Queries;

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
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Paging;
using Domain.Shared.Results;
using Domain.Services.Users;

/// <summary>获取用户详情查询处理器。</summary>
public sealed class GetUserDetailQueryHandler : QueryHandler<GetUserDetailQuery, UserDetailVO>
{
    private readonly IAggregateRepository<SysUser> _userRepository;
    private readonly IDomainRepository<SysUserRole> _userRoleRepository;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    public GetUserDetailQueryHandler(
        IAggregateRepository<SysUser> userRepository,
        IDomainRepository<SysUserRole> userRoleRepository,
        IMapper mapper)
    {
        _userRepository = userRepository;
        _userRoleRepository = userRoleRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<UserDetailVO> HandleAsync(GetUserDetailQuery query, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetAsync(query.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, $"用户 {query.Id} 不存在");
        var detail = _mapper.Map<UserDetailVO>(user);
        detail.RoleIds = (await _userRoleRepository.GetListAsync([ur => ur.UserId == query.Id], cancellationToken: cancellationToken))
            .Select(ur => ur.RoleId)
            .Distinct()
            .ToList();
        return detail;
    }
}

/// <summary>分页查询用户处理器。</summary>
public sealed class GetUserPageQueryHandler : QueryHandler<GetUserPageQuery, PagedResult<UserListVO>>
{
    private readonly IAggregateRepository<SysUser> _userRepository;
    private readonly IAggregateRepository<SysRole> _roleRepository;
    private readonly IAggregateTreeRepository<SysDept> _deptRepository;
    private readonly IDomainRepository<SysUserRole> _userRoleRepository;
    private readonly IDataScopeFilter _dataScopeFilter;
    private readonly UserContext _currentUser;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    public GetUserPageQueryHandler(
        IAggregateRepository<SysUser> userRepository,
        IAggregateRepository<SysRole> roleRepository,
        IAggregateTreeRepository<SysDept> deptRepository,
        IDomainRepository<SysUserRole> userRoleRepository,
        IDataScopeFilter dataScopeFilter,
        UserContext currentUser,
        IMapper mapper)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _deptRepository = deptRepository;
        _userRoleRepository = userRoleRepository;
        _dataScopeFilter = dataScopeFilter;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<PagedResult<UserListVO>> HandleAsync(GetUserPageQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;

        // 数据范围解析（请求内缓存）；筛选部门子树仅在实际传入 deptId 时查询（含自身 + 全部后代）
        var scope = await _dataScopeFilter.GetUserScopeAsync(cancellationToken);
        List<Guid>? deptFilterIds = null;
        if (request.DeptId is not null)
        {
            deptFilterIds = [request.DeptId.Value];
            deptFilterIds.AddRange(
                (await _deptRepository.GetDescendantsAsync(request.DeptId.Value, cancellationToken))
                    .Select(d => d.Id));
        }

        var predicate = BuildPredicate(request, scope, deptFilterIds);
        var page = await _userRepository.GetPagedAsync(request.PageIndex, request.PageSize, [predicate], cancellationToken: cancellationToken);
        if (page.Items.Count == 0)
        {
            return PagedResult<UserListVO>.Create([], page.Total, request);
        }

        // 关联数据一次性批量查询组装（避免 N+1）：部门名 + 角色
        var users = page.Items;
        var userIds = users.Select(u => u.Id).ToList();
        var userRoles = await _userRoleRepository.GetListAsync([ur => userIds.Contains(ur.UserId)], cancellationToken: cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();
        var roleMap = (roleIds.Count == 0
                ? []
                : await _roleRepository.GetListAsync([r => roleIds.Contains(r.Id)], cancellationToken: cancellationToken))
            .ToDictionary(r => r.Id, r => r);

        var relatedDeptIds = users.Where(u => u.DeptId != null).Select(u => u.DeptId!.Value).Distinct().ToList();
        var deptMap = (relatedDeptIds.Count == 0
                ? []
                : await _deptRepository.GetListAsync([d => relatedDeptIds.Contains(d.Id)], cancellationToken: cancellationToken))
            .ToDictionary(d => d.Id, d => d);

        var items = users.Select(user =>
        {
            var vo = _mapper.Map<UserListVO>(user);
            vo.DeptName = user.DeptId != null && deptMap.TryGetValue(user.DeptId.Value, out var dept)
                ? dept.DeptName
                : null;
            vo.Roles = userRoles
                .Where(ur => ur.UserId == user.Id && roleMap.ContainsKey(ur.RoleId))
                .Select(ur => new UserRoleItemVO
                {
                    Id = ur.RoleId,
                    RoleName = roleMap[ur.RoleId].RoleName
                })
                .ToList();
            return vo;
        }).ToList();

        return PagedResult<UserListVO>.Create(items, page.Total, request);
    }

    /// <summary>构造分页查询条件：关键字 / 部门子树 / 状态与数据范围合并为单一表达式。</summary>
    private Expression<Func<SysUser, bool>> BuildPredicate(UserPageRequest request, UserDataScope scope, List<Guid>? deptFilterIds)
    {
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        var status = request.Status;
        var scopeType = scope.Scope;
        var scopeDeptIds = scope.DeptIds?.ToList();
        var currentUserId = _currentUser.UserId ?? Guid.Empty;
        var currentUserName = _currentUser.UserName ?? string.Empty;
        var currentUserDeptId = _currentUser.DeptId;

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
                || (scopeType == DataScope.Self && (u.Id == currentUserId || u.CreatedBy == currentUserName)));
    }
}
