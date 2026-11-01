
using Application.Contracts.Abstractions;
using Application.Contracts.Menus;

namespace Application.Contracts.Commands;
/// <summary>创建菜单命令（目录必有 path；菜单必有 path 与 component；按钮必有 perms）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record MenuCreateCommand : AggregateCreateCommand<MenuCreateRequest>
{
    public MenuCreateCommand(MenuCreateRequest request) : base(request) { }
}

/// <summary>删除菜单命令（存在子菜单或已被角色引用时拒绝）。</summary>
/// <param name="Id">菜单 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record MenuDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>更新菜单命令（父级不可选自身或自己的后代）。</summary>
/// <param name="Id">菜单 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record MenuUpdateCommand : AggregateUpdateCommand<MenuUpdateRequest>
{
    public MenuUpdateCommand(Guid id, MenuUpdateRequest request) : base(id, request) { }
}
