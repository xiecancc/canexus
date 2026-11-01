
using Application.Contracts.Abstractions;
using Application.Contracts.Users;

namespace Application.Contracts.Commands;
/// <summary>创建用户命令（用户名唯一；密码缺省 "123456"）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record UserCreateCommand : AggregateCreateCommand<UserCreateRequest>
{
    public UserCreateCommand(UserCreateRequest request) : base(request) { }
}

/// <summary>删除用户命令（内置超管 admin 与当前登录人自身拒绝删除）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record UserDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>重置用户密码命令（BCrypt 存储）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Request">新密码。</param>
public sealed record UserResetPasswordCommand(Guid Id, UserPasswordRequest Request) : AggregateUpdateCommand<UserPasswordRequest>(Id, Request);

/// <summary>更新用户命令（不含用户名；内置超管 admin 拒绝编辑；角色全量替换）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record UserUpdateCommand : AggregateUpdateCommand<UserUpdateRequest>
{
    public UserUpdateCommand(Guid id, UserUpdateRequest request) : base(id, request) { }
}

/// <summary>分配用户角色命令（全量替换）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Request">角色 Id 集合。</param>
public sealed record UserUpdateRolesCommand(Guid Id, UserRolesRequest Request) : AggregateUpdateCommand<UserRolesRequest>(Id, Request);

/// <summary>修改用户状态命令（内置超管 admin 拒绝操作）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Request">状态请求。</param>
public sealed record UserUpdateStatusCommand(Guid Id, UserStatusRequest Request) : AggregateUpdateCommand<UserStatusRequest>(Id, Request);
