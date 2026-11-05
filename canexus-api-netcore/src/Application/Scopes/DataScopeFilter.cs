
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Users;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Scopes;
/// <summary>
/// 数据范围过滤抽象：根据当前用户的角色数据范围（DataScope）在查询时追加过滤条件。
/// <para>范围取用户全部启用角色中最宽松者（枚举值最小，All=1 最宽松）；
/// 无角色 / 未认证时按仅本人（Self）兜底，避免越权查看；超管（super_admin）恒为 All 不过滤。</para>
/// </summary>
public interface IDataScopeFilter
{
    /// <summary>当前用户生效的数据范围（取其角色中最宽松者）；尚未加载（未触发异步解析）时为 null。</summary>
    public DataScope? CurrentScope { get; }

    /// <summary>解析当前用户的数据范围（按请求内缓存，角色集合仅查询一次）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>数据范围描述（范围类型 + 自定义 / 本部门及以下的部门 Id 集合）。</returns>
    public Task<UserDataScope> GetUserScopeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验目标记录是否在当前用户数据范围内，越界抛出 <see cref="BusinessException"/>（AuthForbidden）。
    /// <para>统一数据范围准入机制（P0-S7）：列表查询按范围收敛结果集（如 UserQueryPaged），
    /// 详情 / 单条查询以本方法做越权准入判定，防止"列表看不到但按 Id 直读"的数据范围绕过。</para>
    /// </summary>
    /// <param name="deptId">目标记录的部门归属（Self 范围不校验）。</param>
    /// <param name="ownerId">目标记录的业务归属人 Id（Self 范围须与当前用户一致）。</param>
    /// <param name="creatorId">目标记录的创建人（可选；Self 范围与创建人一致时放行，与列表口径对齐）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务；越界时抛出业务异常。</returns>
    public Task EnsureInScopeAsync(
        Guid? deptId,
        Guid? ownerId,
        Guid? creatorId = null,
        CancellationToken cancellationToken = default);
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
/// <remarks>构造函数。</remarks>
public sealed class DataScopeFilter(
    IDomainRepository<SysRole> roleRepository,
    IAggregateTreeRepository<SysDept> deptRepository,
    IDomainRepository<SysRoleDept> roleDeptRepository,
    IUserContext currentUser) : IDataScopeFilter
{
    /// <summary>请求内缓存：已解析的数据范围。</summary>
    private UserDataScope? _scope;

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

    /// <inheritdoc />
    public async Task EnsureInScopeAsync(
        Guid? deptId,
        Guid? ownerId,
        Guid? creatorId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await GetUserScopeAsync(cancellationToken);
        if (scope.IsAll)
        {
            return;
        }

        var currentUserId = currentUser.UserId ?? Guid.Empty;
        var allowed = scope.Scope switch
        {
            // 仅本人：业务归属人或创建人为当前用户（与列表查询的 Self 口径一致）
            DataScope.Self => ownerId == currentUserId || creatorId == currentUserId,

            // 本部门：目标记录部门与当前用户部门一致
            DataScope.Dept => deptId is not null && deptId == currentUser.DeptId,

            // 本部门及以下 / 自定义：目标记录部门在已解析的部门 Id 集合内
            DataScope.DeptAndBelow or DataScope.Custom =>
                deptId is not null && scope.DeptIds is not null && scope.DeptIds.Contains(deptId.Value),

            // 未知范围值按拒绝处理（fail-closed）
            _ => false,
        };

        if (!allowed)
        {
            throw new BusinessException(ErrorCodes.AuthForbidden, "无权访问该数据（超出数据范围）");
        }
    }

    /// <summary>解析当前用户的数据范围：超管 → All；无角色 / 未认证 → Self；否则取启用角色中最宽松范围。</summary>
    private async Task<UserDataScope> ResolveScopeAsync(CancellationToken cancellationToken)
    {
        // 超管放行全部数据
        if (currentUser.IsSuperAdmin)
        {
            return new UserDataScope(DataScope.All, null);
        }

        var roleIds = currentUser.RoleIds.ToList();
        if (roleIds.Count == 0)
        {
            return new UserDataScope(DataScope.Self, null);
        }

        var roles = await roleRepository.GetListAsync(
            [r => r.Status == CommonStatus.Enabled && roleIds.Contains(r.Id)],
            cancellationToken: cancellationToken);
        if (roles.Count == 0)
        {
            return new UserDataScope(DataScope.Self, null);
        }

        // 最宽松范围 = 枚举最小值（All=1 最宽松，Self=5 最严格）
        var scope = roles.Min(r => r.DataScope);
        return scope switch
        {
            DataScope.All => new UserDataScope(DataScope.All, null),

            // 未分配部门时部门维度无法圈定，按仅本人兜底
            DataScope.Dept when currentUser.DeptId is null => new UserDataScope(DataScope.Self, null),
            DataScope.DeptAndBelow when currentUser.DeptId is null => new UserDataScope(DataScope.Self, null),

            DataScope.Dept => new UserDataScope(DataScope.Dept, null),

            // 树形仓储下探：本部门及其全部后代（含自身）
            DataScope.DeptAndBelow => new UserDataScope(
                DataScope.DeptAndBelow,
                await CollectDeptAndBelowAsync(currentUser.DeptId!.Value, cancellationToken)),

            DataScope.Custom => await ResolveCustomScopeAsync(roles, cancellationToken),

            _ => new UserDataScope(DataScope.Self, null),
        };
    }

    /// <summary>下探本部门及其全部后代（含自身）的部门 Id 集合。</summary>
    private async Task<List<Guid>> CollectDeptAndBelowAsync(Guid deptId, CancellationToken cancellationToken)
    {
        List<Guid> belowIds = [deptId];
        belowIds.AddRange(
            (await deptRepository.GetDescendantsAsync(deptId, cancellationToken))
                .Select(d => d.Id));
        return belowIds;
    }

    /// <summary>解析自定义数据范围：取全部自定义角色的部门 Id 并集；为空时按仅本人兜底。</summary>
    private async Task<UserDataScope> ResolveCustomScopeAsync(List<SysRole> roles, CancellationToken cancellationToken)
    {
        var customRoleIds = roles
            .Where(r => r.DataScope == DataScope.Custom)
            .Select(r => r.Id)
            .ToList();

        var deptIds = (await roleDeptRepository.GetListAsync(
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
