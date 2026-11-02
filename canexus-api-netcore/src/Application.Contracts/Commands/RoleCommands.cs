
using Application.Contracts.Abstractions;
using Application.Contracts.Roles;

namespace Application.Contracts.Commands;
/// <summary>创建角色命令（roleKey 唯一且不可使用保留值 super_admin；菜单授权全删全插）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record RoleCreateCommand : AggregateCreateCommand<RoleInputRequest>
{
    public RoleCreateCommand(RoleInputRequest request) : base(request) { }
}

/// <summary>删除角色命令（内置 super_admin 角色拒绝删除；已被用户引用拒绝删除）。</summary>
/// <param name="Id">角色 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record RoleDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>恢复角色命令（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
/// <param name="Id">角色 Id。</param>
/// <param name="Version">乐观锁版本号（与删除相同的路由参数模式）。</param>
public sealed record RoleRestoreCommand(Guid Id, Guid Version) : AggregateRestoreCommand(Id, Version);

/// <summary>更新角色命令（内置 super_admin 角色整体拒绝编辑）。</summary>
/// <param name="Id">角色 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
/// <param name="Request">更新请求。</param>
public sealed record RoleUpdateCommand : AggregateUpdateCommand<RoleInputRequest>
{
    public RoleUpdateCommand(Guid id, Guid version, RoleInputRequest request) : base(id, version, request) { }
}
