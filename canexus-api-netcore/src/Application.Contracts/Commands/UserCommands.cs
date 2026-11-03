
using Application.Contracts.Abstractions;
using Application.Contracts.Users;

namespace Application.Contracts.Commands;
/// <summary>创建用户命令（用户名唯一；密码缺省 "123456"）。</summary>
public sealed record UserCreateCommand : AggregateCreateCommand<UserInputRequest>
{
    /// <summary>构造创建用户命令。</summary>
    /// <param name="request">创建请求。</param>
    public UserCreateCommand(UserInputRequest request) : base(request) { }
}

/// <summary>删除用户命令（内置超管 admin 与当前登录人自身拒绝删除）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record UserDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>恢复用户命令（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Version">乐观锁版本号（与删除相同的路由参数模式）。</param>
public sealed record UserRestoreCommand(Guid Id, Guid Version) : AggregateRestoreCommand(Id, Version);

/// <summary>重置用户密码命令（BCrypt 存储）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
/// <param name="Request">新密码。</param>
public sealed record UserResetPasswordCommand(Guid Id, Guid Version, UserPasswordRequest Request) : AggregateUpdateCommand<UserPasswordRequest>(Id, Version, Request);

/// <summary>更新用户命令（不含用户名；内置超管 admin 拒绝编辑；角色全量替换）。</summary>
public sealed record UserUpdateCommand : AggregateUpdateCommand<UserInputRequest>
{
    /// <summary>构造更新用户命令。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">更新请求。</param>
    public UserUpdateCommand(Guid id, Guid version, UserInputRequest request) : base(id, version, request) { }
}

/// <summary>分配用户角色命令（全量替换）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
/// <param name="Request">角色 Id 集合。</param>
public sealed record UserUpdateRolesCommand(Guid Id, Guid Version, UserRolesRequest Request) : AggregateUpdateCommand<UserRolesRequest>(Id, Version, Request);

/// <summary>修改用户状态命令（内置超管 admin 拒绝操作）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
/// <param name="Request">状态请求。</param>
public sealed record UserUpdateStatusCommand(Guid Id, Guid Version, UserStatusRequest Request) : AggregateUpdateCommand<UserStatusRequest>(Id, Version, Request);
