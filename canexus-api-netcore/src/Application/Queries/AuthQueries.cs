
using Application.Abstractions;
using Application.Contracts.Abstractions;
using Application.Contracts.Auth;
using Application.Contracts.Menus;
using Application.Contracts.Queries;
using Application.Permissions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Services.Security;
using Domain.Services.Users;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Queries;
/// <summary>获取图形验证码查询处理器。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="captchaService">图形验证码服务。</param>
public sealed class AuthQueryCaptchaHandler(ICaptchaService captchaService) : QueryHandler<AuthQueryCaptcha, CaptchaResult>
{
    /// <inheritdoc />
    protected override async Task<CaptchaResult> HandleAsync(AuthQueryCaptcha query, CancellationToken cancellationToken)
    {
        var image = await captchaService.GenerateAsync(cancellationToken);
        return new CaptchaResult { CaptchaId = image.CaptchaId, ImageBase64 = image.ImageBase64 };
    }
}

/// <summary>
/// 获取当前用户前端路由树查询处理器：
/// <list type="bullet">
/// <item>超管可见全部菜单，普通用户按 角色 → 角色菜单 过滤；</item>
/// <item>仅取目录与菜单类型、可见（Show）且启用（Enabled）的菜单；</item>
/// <item>路由 name 由完整路径段拼接驼峰（如 /system/user → SystemUser），保证全局唯一。</item>
/// </list>
/// </summary>
/// <remarks>构造函数。</remarks>
public sealed class UserQueryRoutesHandler(
    IAggregateTreeRepository<SysMenu> menuRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IDomainRepository<SysRoleMenu> roleMenuRepository,
    IUserContext currentUser) : QueryHandler<UserQueryRoutes, List<RouteResponse>>
{
    /// <summary>根节点父 Id（Guid.Empty 表示无父级）。</summary>
    private static readonly Guid RootParentId = Guid.Empty;

    /// <inheritdoc />
    protected override async Task<List<RouteResponse>> HandleAsync(UserQueryRoutes query, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new BusinessException(ErrorCodes.AuthUnauthorized, "未登录或令牌无效");

        var menus = currentUser.IsSuperAdmin
            ? await menuRepository.GetListAsync(cancellationToken: cancellationToken)
            : await GetUserMenusAsync(userId, cancellationToken);

        var visible = menus
            .Where(m => m.Status == CommonStatus.Enabled
                && m.Visible == MenuVisible.Show
                && (m.MenuType is MenuType.Directory or MenuType.Menu))
            .OrderBy(m => m.OrderNum)
            .ThenBy(m => m.Id)
            .ToList();

        return BuildTree(visible, RootParentId);
    }

    /// <summary>查询普通用户可见菜单：角色 → 角色菜单 → 菜单。</summary>
    private async Task<List<SysMenu>> GetUserMenusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roleIds = (await userRoleRepository.GetListAsync([ur => ur.UserId == userId], cancellationToken: cancellationToken))
            .Select(ur => ur.RoleId)
            .Distinct()
            .ToList();
        if (roleIds.Count == 0)
        {
            return [];
        }

        var menuIds = (await roleMenuRepository.GetListAsync([rm => roleIds.Contains(rm.RoleId)], cancellationToken: cancellationToken))
            .Select(rm => rm.MenuId)
            .Distinct()
            .ToList();
        if (menuIds.Count == 0)
        {
            return [];
        }

        return await menuRepository.GetListAsync([m => menuIds.Contains(m.Id)], cancellationToken: cancellationToken);
    }

    /// <summary>按父 Id 递归构建路由树。</summary>
    /// <param name="menus">参与构建的菜单集合。</param>
    /// <param name="parentId">当前父菜单 Id。</param>
    /// <param name="parentPath">父级完整路径（顶层为 null）。</param>
    private List<RouteResponse> BuildTree(List<SysMenu> menus, Guid parentId, string? parentPath = null)
    {
        var children = menus.Where(m => m.ParentId == parentId).ToList();
        var result = new List<RouteResponse>(children.Count);

        foreach (var menu in children)
        {
            // 顶层路径补 / 前缀；子级拼接到父路径后，用于生成全局唯一的路由 name（如 /system/user → SystemUser）
            var fullPath = parentPath is null
                ? (menu.Path.StartsWith('/') ? menu.Path : $"/{menu.Path}")
                : $"{parentPath}/{menu.Path}";
            var subTree = BuildTree(menus, menu.Id, fullPath);
            var route = ToRouteResponse(menu, subTree, fullPath, parentPath is null);
            result.Add(route);
        }

        return result;
    }

    /// <summary>菜单转路由视图对象。</summary>
    /// <param name="menu">菜单实体。</param>
    /// <param name="children">已构建的子路由。</param>
    /// <param name="fullPath">完整路径（用于生成路由 name）。</param>
    /// <param name="isTopLevel">是否顶层节点（path 补 / 前缀）。</param>
    private static RouteResponse ToRouteResponse(SysMenu menu, List<RouteResponse> children, string fullPath, bool isTopLevel)
    {
        return new RouteResponse
        {
            Name = BuildRouteName(fullPath),
            Path = isTopLevel ? fullPath : menu.Path,
            Component = menu.MenuType == MenuType.Menu ? menu.Component : null,
            Redirect = children.Count > 0 ? children[0].Path : null,
            Meta = new RouteMeta
            {
                Title = menu.MenuName,
                Icon = menu.Icon,
                KeepAlive = menu.KeepAlive,
                Hidden = false,
                Order = menu.OrderNum
            },
            Children = children
        };
    }

    /// <summary>由完整路径生成驼峰路由名（/system/user → SystemUser，/dashboard → Dashboard）。</summary>
    private static string BuildRouteName(string fullPath)
    {
        var segments = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(segments.Select(segment =>
            char.ToUpperInvariant(segment[0]) + segment[1..]));
    }
}

/// <summary>获取当前登录用户信息查询处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UserQueryInfoHandler(
    IAggregateRepository<SysUser> userRepository,
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IPermissionChecker permissionChecker,
    IUserContext currentUser) : QueryHandler<UserQueryInfo, UserInfoResult>
{
    /// <inheritdoc />
    protected override async Task<UserInfoResult> HandleAsync(UserQueryInfo query, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new BusinessException(ErrorCodes.AuthUnauthorized, "未登录或令牌无效");

        var user = await userRepository.GetAsync([it => it.Id == userId], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.AuthUnauthorized, "登录状态已失效，请重新登录");

        var roles = (await GetUserRolesAsync(userId, cancellationToken)).Select(r => r.RoleKey).ToList();
        var permissions = await permissionChecker.GetPermissionsAsync(userId, cancellationToken);

        return new UserInfoResult
        {
            UserId = user.Id,
            UserName = user.UserName,
            NickName = user.NickName,
            Avatar = user.Avatar,
            Roles = roles,
            Permissions = permissions.ToList()
        };
    }

    /// <summary>查询用户启用状态的角色列表。</summary>
    private async Task<List<SysRole>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roleIds = (await userRoleRepository.GetListAsync([ur => ur.UserId == userId], cancellationToken: cancellationToken))
            .Select(ur => ur.RoleId)
            .Distinct()
            .ToList();
        if (roleIds.Count == 0)
        {
            return [];
        }

        return await roleRepository.GetListAsync([r => r.Status == CommonStatus.Enabled && roleIds.Contains(r.Id)], cancellationToken: cancellationToken);
    }
}
