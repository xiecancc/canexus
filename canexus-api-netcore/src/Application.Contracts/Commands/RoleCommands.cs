
using Application.Contracts.Abstractions;
using Application.Contracts.Roles;
using Domain.Shared.Permissions;

namespace Application.Contracts.Commands;
/// <summary>创建角色命令（roleKey 唯一且不可使用保留值 super_admin；菜单授权全删全插）。</summary>
[RequireCommandPermission(PermissionDefinitions.SystemRoleAdd)]
public sealed record RoleCreateCommand : AggregateCreateCommand<RoleInputRequest>
{
    /// <summary>构造创建角色命令。</summary>
    /// <param name="request">创建请求。</param>
    public RoleCreateCommand(RoleInputRequest request) : base(request) { }
}

/// <summary>删除角色命令（内置 super_admin 角色拒绝删除；已被用户引用拒绝删除）。</summary>
/// <param name="Id">角色 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
[RequireCommandPermission(PermissionDefinitions.SystemRoleRemove)]
public sealed record RoleDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>恢复角色命令（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
/// <param name="Id">角色 Id。</param>
/// <param name="Version">乐观锁版本号（与删除相同的路由参数模式）。</param>
[RequireCommandPermission(PermissionDefinitions.SystemRoleRestore)]
public sealed record RoleRestoreCommand(Guid Id, Guid Version) : AggregateRestoreCommand(Id, Version);

/// <summary>更新角色命令（内置 super_admin 角色整体拒绝编辑）。</summary>
[RequireCommandPermission(PermissionDefinitions.SystemRoleEdit)]
public sealed record RoleUpdateCommand : AggregateUpdateCommand<RoleInputRequest>
{
    /// <summary>构造更新角色命令。</summary>
    /// <param name="id">角色 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">更新请求。</param>
    public RoleUpdateCommand(Guid id, Guid version, RoleInputRequest request) : base(id, version, request) { }
}

/// <summary>修改角色状态命令（独立启停：停用后权限缓存由监听器全量失效即时生效；super_admin 拒绝启停）。</summary>
[RequireCommandPermission(PermissionDefinitions.SystemRoleEdit)]
public sealed record RoleUpdateStatusCommand : AggregateUpdateCommand<RoleStatusRequest>
{
    /// <summary>构造修改角色状态命令。</summary>
    /// <param name="id">角色 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">状态请求。</param>
    public RoleUpdateStatusCommand(Guid id, Guid version, RoleStatusRequest request) : base(id, version, request) { }
}
