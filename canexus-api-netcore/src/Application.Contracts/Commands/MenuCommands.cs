
using Application.Contracts.Abstractions;
using Application.Contracts.Menus;
using Domain.Shared.Permissions;

namespace Application.Contracts.Commands;
/// <summary>创建菜单命令（目录必有 path；菜单必有 path 与 component；按钮必有 perms）。</summary>
[RequireCommandPermission(PermissionDefinitions.SystemMenuAdd)]
public sealed record MenuCreateCommand : AggregateCreateCommand<MenuInputRequest>
{
    /// <summary>构造创建菜单命令。</summary>
    /// <param name="request">创建请求。</param>
    public MenuCreateCommand(MenuInputRequest request) : base(request) { }
}

/// <summary>删除菜单命令（存在子菜单或已被角色引用时拒绝）。</summary>
/// <param name="Id">菜单 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
[RequireCommandPermission(PermissionDefinitions.SystemMenuRemove)]
public sealed record MenuDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>恢复菜单命令（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
/// <param name="Id">菜单 Id。</param>
/// <param name="Version">乐观锁版本号（与删除相同的路由参数模式）。</param>
[RequireCommandPermission(PermissionDefinitions.SystemMenuRestore)]
public sealed record MenuRestoreCommand(Guid Id, Guid Version) : AggregateRestoreCommand(Id, Version);

/// <summary>更新菜单命令（父级不可选自身或自己的后代）。</summary>
[RequireCommandPermission(PermissionDefinitions.SystemMenuEdit)]
public sealed record MenuUpdateCommand : AggregateUpdateCommand<MenuInputRequest>
{
    /// <summary>构造更新菜单命令。</summary>
    /// <param name="id">菜单 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">更新请求。</param>
    public MenuUpdateCommand(Guid id, Guid version, MenuInputRequest request) : base(id, version, request) { }
}
