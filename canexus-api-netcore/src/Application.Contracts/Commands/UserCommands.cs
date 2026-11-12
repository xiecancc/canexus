
using Application.Contracts.Abstractions;
using Application.Contracts.Users;
using Domain.Shared.Permissions;

namespace Application.Contracts.Commands;
/// <summary>创建用户命令（用户名唯一；密码缺省取参数配置 sys.user.initPassword，一律首登强制改密）。</summary>
[RequireCommandPermission(PermissionDefinitions.SystemUserAdd)]
public sealed record UserCreateCommand : AggregateCreateCommand<UserInputRequest>
{
    /// <summary>构造创建用户命令。</summary>
    /// <param name="request">创建请求。</param>
    public UserCreateCommand(UserInputRequest request) : base(request) { }
}

/// <summary>批量导入用户命令（Excel 流经 MiniExcel 解析；行级校验全部通过才入库，任一行失败整体回执不入库）。</summary>
/// <param name="Content">Excel 文件内容流（xlsx）。</param>
[RequireCommandPermission(PermissionDefinitions.SystemUserImport)]
public sealed record UserImportCommand(Stream Content) : Request<UserImportResultDto>, ICommand;

/// <summary>删除用户命令（内置超管 admin 与当前登录人自身拒绝删除）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
[RequireCommandPermission(PermissionDefinitions.SystemUserRemove)]
public sealed record UserDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version), IUserDataScopeCommand
{
    /// <inheritdoc />
    public Guid TargetUserId => Id;
}

/// <summary>恢复用户命令（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Version">乐观锁版本号（与删除相同的路由参数模式）。</param>
[RequireCommandPermission(PermissionDefinitions.SystemUserRestore)]
public sealed record UserRestoreCommand(Guid Id, Guid Version) : AggregateRestoreCommand(Id, Version), IUserDataScopeCommand
{
    /// <inheritdoc />
    public Guid TargetUserId => Id;
}

/// <summary>重置用户密码命令（BCrypt 存储）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
/// <param name="Request">新密码。</param>
[RequireCommandPermission(PermissionDefinitions.SystemUserResetPwd)]
public sealed record UserResetPasswordCommand(Guid Id, Guid Version, UserPasswordRequest Request) : AggregateUpdateCommand<UserPasswordRequest>(Id, Version, Request), IUserDataScopeCommand
{
    /// <inheritdoc />
    public Guid TargetUserId => Id;
}

/// <summary>更新用户命令（不含用户名；内置超管 admin 拒绝编辑；角色全量替换）。</summary>
[RequireCommandPermission(PermissionDefinitions.SystemUserEdit)]
public sealed record UserUpdateCommand : AggregateUpdateCommand<UserInputRequest>, IUserDataScopeCommand
{
    /// <summary>构造更新用户命令。</summary>
    /// <param name="id">用户 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">更新请求。</param>
    public UserUpdateCommand(Guid id, Guid version, UserInputRequest request) : base(id, version, request) { }

    /// <inheritdoc />
    public Guid TargetUserId => Id;
}

/// <summary>分配用户角色命令（全量替换）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
/// <param name="Request">角色 Id 集合。</param>
[RequireCommandPermission(PermissionDefinitions.SystemUserEdit)]
public sealed record UserUpdateRolesCommand(Guid Id, Guid Version, UserRolesRequest Request) : AggregateUpdateCommand<UserRolesRequest>(Id, Version, Request), IUserDataScopeCommand
{
    /// <inheritdoc />
    public Guid TargetUserId => Id;
}

/// <summary>修改用户状态命令（内置超管 admin 拒绝操作）。</summary>
/// <param name="Id">用户 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
/// <param name="Request">状态请求。</param>
[RequireCommandPermission(PermissionDefinitions.SystemUserEdit)]
public sealed record UserUpdateStatusCommand(Guid Id, Guid Version, UserStatusRequest Request) : AggregateUpdateCommand<UserStatusRequest>(Id, Version, Request), IUserDataScopeCommand
{
    /// <inheritdoc />
    public Guid TargetUserId => Id;
}
