
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Enums;

namespace Application.Scopes;
/// <summary>
/// 数据范围过滤抽象：根据当前用户的角色数据范围（DataScope）在查询时追加过滤条件。
/// <para>范围取用户全部启用角色中最宽松者（枚举值最小，All=1 最宽松）；
/// 无角色 / 未认证时按仅本人（Self）兜底，避免越权查看；超管（super_admin）恒为 All 不过滤。</para>
/// </summary>
public interface IDataScopeFilter
{
    /// <summary>当前用户生效的数据范围（取其角色中最宽松者）；尚未加载（未触发异步解析）时为 null。</summary>
    DataScope? CurrentScope { get; }

    /// <summary>解析当前用户的数据范围（按请求内缓存，角色集合仅查询一次）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>数据范围描述（范围类型 + 自定义 / 本部门及以下的部门 Id 集合）。</returns>
    Task<UserDataScope> GetUserScopeAsync(CancellationToken cancellationToken = default);
}

/// <summary>用户数据范围描述（供查询方构造过滤条件）。</summary>
/// <param name="Scope">生效的数据范围。</param>
/// <param name="DeptIds">部门 Id 集合：Custom 为角色自定义部门并集；DeptAndBelow 为本部门及下属部门全集；其余范围为 null。</param>
public sealed record UserDataScope(DataScope Scope, IReadOnlyList<Guid>? DeptIds)
{
    /// <summary>是否全部数据权限（不过滤）。</summary>
    public bool IsAll => Scope == DataScope.All;
}

/// <summary>数据范围过滤实现：基于当前用户角色解析最宽数据范围，用户查询按此追加过滤条件。</summary>
public sealed class DataScopeFilter : IDataScopeFilter
{
    private readonly IDomainRepository<SysRole> _roleRepository;
    private readonly IAggregateTreeRepository<SysDept> _deptRepository;
    private readonly IDomainRepository<SysRoleDept> _roleDeptRepository;
    private readonly UserContext _currentUser;

    /// <summary>请求内缓存：已解析的数据范围。</summary>
    private UserDataScope? _scope;

    /// <summary>构造函数。</summary>
    public DataScopeFilter(
        IDomainRepository<SysRole> roleRepository,
        IAggregateTreeRepository<SysDept> deptRepository,
        IDomainRepository<SysRoleDept> roleDeptRepository,
        UserContext currentUser)
    {
        _roleRepository = roleRepository;
        _deptRepository = deptRepository;
        _roleDeptRepository = roleDeptRepository;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public DataScope? CurrentScope => _scope?.Scope;

    /// <inheritdoc />
    public async Task<UserDataScope> GetUserScopeAsync(CancellationToken cancellationToken = default)
    {
        if (_scope is not null)
        {
            return _scope;
        }

        _scope = await ResolveScopeAsync(cancellationToken);
        return _scope;
    }

    /// <summary>解析当前用户的数据范围：超管 → All；无角色 / 未认证 → Self；否则取启用角色中最宽松范围。</summary>
    private async Task<UserDataScope> ResolveScopeAsync(CancellationToken cancellationToken)
    {
        // 超管放行全部数据
        if (_currentUser.IsSuperAdmin)
        {
            return new UserDataScope(DataScope.All, null);
        }

        var roleIds = _currentUser.RoleIds.ToList();
        if (roleIds.Count == 0)
        {
            return new UserDataScope(DataScope.Self, null);
        }

        var roles = await _roleRepository.GetListAsync(
            [r => r.Status == CommonStatus.Enabled && roleIds.Contains(r.Id)],
            cancellationToken: cancellationToken);
        if (roles.Count == 0)
        {
            return new UserDataScope(DataScope.Self, null);
        }

        // 最宽松范围 = 枚举最小值（All=1 最宽松，Self=5 最严格）
        var scope = roles.Min(r => r.DataScope);
        switch (scope)
        {
            case DataScope.All:
                return new UserDataScope(DataScope.All, null);

            // 未分配部门时部门维度无法圈定，按仅本人兜底
            case DataScope.Dept when _currentUser.DeptId is null:
            case DataScope.DeptAndBelow when _currentUser.DeptId is null:
                return new UserDataScope(DataScope.Self, null);

            case DataScope.Dept:
                return new UserDataScope(DataScope.Dept, null);

            case DataScope.DeptAndBelow:
                // 树形仓储下探：本部门及其全部后代（含自身）
                var belowIds = new List<Guid> { _currentUser.DeptId!.Value };
                belowIds.AddRange(
                    (await _deptRepository.GetDescendantsAsync(_currentUser.DeptId.Value, cancellationToken))
                        .Select(d => d.Id));
                return new UserDataScope(DataScope.DeptAndBelow, belowIds);

            case DataScope.Custom:
                return await ResolveCustomScopeAsync(roles, cancellationToken);

            default:
                return new UserDataScope(DataScope.Self, null);
        }
    }

    /// <summary>解析自定义数据范围：取全部自定义角色的部门 Id 并集；为空时按仅本人兜底。</summary>
    private async Task<UserDataScope> ResolveCustomScopeAsync(List<SysRole> roles, CancellationToken cancellationToken)
    {
        var customRoleIds = roles
            .Where(r => r.DataScope == DataScope.Custom)
            .Select(r => r.Id)
            .ToList();

        var deptIds = (await _roleDeptRepository.GetListAsync(
                [rd => customRoleIds.Contains(rd.RoleId)],
                cancellationToken: cancellationToken))
            .Select(rd => rd.DeptId)
            .Distinct()
            .ToList();

        return deptIds.Count == 0
            ? new UserDataScope(DataScope.Self, null)
            : new UserDataScope(DataScope.Custom, deptIds);
    }
}
