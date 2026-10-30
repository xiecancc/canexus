namespace Application.Contracts.Commands;

using Application.Contracts.Abstractions;
using Application.Contracts.Menus;

/// <summary>创建菜单命令（目录必有 path；菜单必有 path 与 component；按钮必有 perms）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record CreateMenuCommand(MenuCreateRequest Request) : ICommand<Guid>;

/// <summary>删除菜单命令（存在子菜单或已被角色引用时拒绝）。</summary>
/// <param name="Id">菜单 Id。</param>
public sealed record DeleteMenuCommand(Guid Id) : ICommand;

/// <summary>更新菜单命令（父级不可选自身或自己的后代）。</summary>
/// <param name="Id">菜单 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record UpdateMenuCommand(Guid Id, MenuUpdateRequest Request) : ICommand;
