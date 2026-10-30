namespace Application.Contracts.Commands;

using Application.Contracts.Abstractions;
using Application.Contracts.Roles;

/// <summary>创建角色命令（roleKey 唯一且不可使用保留值 super_admin；菜单授权全删全插）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record CreateRoleCommand(RoleCreateRequest Request) : ICommand<Guid>;

/// <summary>删除角色命令（内置 super_admin 角色拒绝删除；已被用户引用拒绝删除）。</summary>
/// <param name="Id">角色 Id。</param>
public sealed record DeleteRoleCommand(Guid Id) : ICommand;

/// <summary>更新角色命令（内置 super_admin 角色整体拒绝编辑）。</summary>
/// <param name="Id">角色 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record UpdateRoleCommand(Guid Id, RoleUpdateRequest Request) : ICommand;
