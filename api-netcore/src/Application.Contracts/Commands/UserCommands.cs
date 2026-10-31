
using Application.Contracts.Abstractions;
using Application.Contracts.Users;

namespace Application.Contracts.Commands;
/// <summary>创建用户命令（用户名唯一；密码缺省 "123456"）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record CreateUserCommand(UserCreateRequest Request) : ICommand<Guid>;

/// <summary>删除用户命令（内置超管 admin 与当前登录人自身拒绝删除）。</summary>
/// <param name="Id">用户 Id。</param>
public sealed record DeleteUserCommand(Guid Id) : ICommand;

/// <summary>重置用户密码命令（BCrypt 存储）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Request">新密码。</param>
public sealed record ResetUserPasswordCommand(Guid Id, UserPasswordRequest Request) : ICommand;

/// <summary>更新用户命令（不含用户名；内置超管 admin 拒绝编辑；角色全量替换）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record UpdateUserCommand(Guid Id, UserUpdateRequest Request) : ICommand;

/// <summary>分配用户角色命令（全量替换）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Request">角色 Id 集合。</param>
public sealed record UpdateUserRolesCommand(Guid Id, UserRolesRequest Request) : ICommand;

/// <summary>修改用户状态命令（内置超管 admin 拒绝操作）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Request">状态请求。</param>
public sealed record UpdateUserStatusCommand(Guid Id, UserStatusRequest Request) : ICommand;
