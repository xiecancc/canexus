
using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Users;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Auth;
using Domain.Services.Events;
using Domain.Services.Users;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Commands;
/// <summary>用户-角色关联的共享业务逻辑（角色存在性校验与全量替换，供多个用户命令处理器复用）。</summary>
internal static class UserRoleSupport
{
    /// <summary>校验角色 Id 集合全部存在。</summary>
    public static async Task ValidateRolesAsync(IAggregateRepository<SysRole> roleRepository, List<Guid> roleIds, CancellationToken cancellationToken)
    {
        var distinct = roleIds.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return;
        }

        var count = await roleRepository.CountAsync([r => distinct.Contains(r.Id)], cancellationToken);
        if (count < distinct.Count)
        {
            throw new BusinessException(ErrorCodes.CommonDataNotFound, "所选角色不存在或已被删除");
        }
    }

    /// <summary>全量替换用户-角色关联（批量删除 + 批量插入，调用方需保证工作单元事务内执行）。</summary>
    public static async Task ReplaceUserRolesAsync(IDomainRepository<SysUserRole> userRoleRepository, Guid userId, List<Guid> roleIds, CancellationToken cancellationToken)
    {
        await userRoleRepository.DeleteRangeAsync(ur => ur.UserId == userId, cancellationToken);
        await userRoleRepository.InsertRangeAsync(
            roleIds.Distinct().Select(roleId => new SysUserRole { UserId = userId, RoleId = roleId }),
            cancellationToken);
    }
}

/// <summary>创建用户命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class CreateUserCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IAggregateRepository<SysRole> roleRepository,
    IAggregateTreeRepository<SysDept> deptRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IPasswordHasher passwordHasher,
    IDomainEventPublisher domainEventPublisher,
    IUnitOfWork unitOfWork) : CommandHandler<CreateUserCommand, Guid>
{
    /// <summary>内置超管账号用户名。</summary>
    public const string AdminUserName = "admin";

    /// <summary>创建用户缺省密码。</summary>
    private const string DefaultPassword = "123456";

    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var userName = request.UserName.Trim();
        if (await userRepository.CountAsync([u => u.UserName == userName], cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.UserNameDuplicate, $"用户名「{userName}」已存在");
        }

        await UserRoleSupport.ValidateRolesAsync(roleRepository, request.RoleIds, cancellationToken);
        if (request.DeptId is not null && await deptRepository.CountAsync([d => d.Id == request.DeptId], cancellationToken) == 0)
        {
            throw new BusinessException(ErrorCodes.CommonDataNotFound, "所属部门不存在");
        }

        var inserted = await unitOfWork.RunInTransactionAsync(async () =>
        {
            var user = new SysUser
            {
                UserName = userName,
                NickName = request.NickName.Trim(),
                // 密码缺省使用默认密码 123456（DTO 注释约定），BCrypt 存储
                PasswordHash = passwordHasher.Hash(
                    string.IsNullOrWhiteSpace(request.Password) ? DefaultPassword : request.Password),
                DeptId = request.DeptId,
                Email = request.Email,
                Phone = request.Phone,
                Status = request.Status,
                Description = request.Description
            };
            var insertedUser = await userRepository.InsertAsync(user, cancellationToken);
            await UserRoleSupport.ReplaceUserRolesAsync(userRoleRepository, insertedUser.Id, request.RoleIds, cancellationToken);
            return insertedUser;
        }, cancellationToken);

        // 关键节点发布用户创建领域事件（携带创建后的用户状态）
        await domainEventPublisher.PublishAsync(new UserCreatedEvent(inserted), cancellationToken);
        return inserted.Id;
    }
}

/// <summary>删除用户命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DeleteUserCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IDomainEventPublisher domainEventPublisher,
    UserContext currentUser,
    IUnitOfWork unitOfWork) : CommandHandler<DeleteUserCommand>
{
    /// <summary>内置超管账号用户名。</summary>
    private const string AdminUserName = "admin";

    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, $"用户 {command.Id} 不存在");
        if (user.UserName == AdminUserName)
        {
            throw new BusinessException(ErrorCodes.UserAdminProtected, "内置超管账号 admin 不允许删除");
        }

        if (currentUser.UserId == command.Id)
        {
            throw new BusinessException(ErrorCodes.UserDeleteSelfForbidden, "不能删除当前登录账号自己");
        }

        // 变更前捕获用户状态快照（软删除可能回填墓碑字段）
        var snapshot = user.Snapshot<SysUser>();

        await unitOfWork.RunInTransactionAsync(async () =>
        {
            // 软删除用户与其角色关联（保持可追溯）
            await userRepository.DeleteAsync(user, cancellationToken);
            var relations = await userRoleRepository.GetListAsync([ur => ur.UserId == command.Id], cancellationToken: cancellationToken);
            foreach (var relation in relations)
            {
                await userRoleRepository.DeleteAsync(relation, cancellationToken);
            }
        }, cancellationToken);

        // 关键节点发布用户删除领域事件（携带删除前的用户状态）
        await domainEventPublisher.PublishAsync(new UserDeletedEvent(snapshot), cancellationToken);
    }
}

/// <summary>重置用户密码命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class ResetUserPasswordCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IPasswordHasher passwordHasher,
    IDomainEventPublisher domainEventPublisher) : CommandHandler<ResetUserPasswordCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(ResetUserPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, $"用户 {command.Id} 不存在");
        user.PasswordHash = passwordHasher.Hash(command.Request.Password);
        await userRepository.UpdateAsync(user, cancellationToken);

        // 关键节点发布用户重置密码领域事件（不携带状态：密码哈希属敏感数据，强制下线等监听器按用户 Id 响应）
        await domainEventPublisher.PublishAsync(new UserPasswordResetEvent(command.Id), cancellationToken);
    }
}

/// <summary>更新用户命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UpdateUserCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IAggregateTreeRepository<SysDept> deptRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IDomainEventPublisher domainEventPublisher,
    IUnitOfWork unitOfWork) : CommandHandler<UpdateUserCommand>
{
    /// <summary>内置超管账号用户名。</summary>
    private const string AdminUserName = "admin";

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var user = await GetOrThrowAsync(command.Id, cancellationToken);
        EnsureNotAdmin(user);

        if (request.DeptId is not null && await deptRepository.CountAsync([d => d.Id == request.DeptId], cancellationToken) == 0)
        {
            throw new BusinessException(ErrorCodes.CommonDataNotFound, "所属部门不存在");
        }

        // 变更前捕获用户状态快照（实体在事务内被原位修改）
        var before = user.Snapshot<SysUser>();

        await unitOfWork.RunInTransactionAsync(async () =>
        {
            user.NickName = request.NickName.Trim();
            user.DeptId = request.DeptId;
            user.Email = request.Email;
            user.Phone = request.Phone;
            user.Status = request.Status;
            user.Description = request.Description;
            await userRepository.UpdateAsync(user, cancellationToken);
            await UserRoleSupport.ReplaceUserRolesAsync(userRoleRepository, command.Id, request.RoleIds, cancellationToken);
        }, cancellationToken);

        // 关键节点发布用户修改领域事件（携带变更前后用户状态）
        await domainEventPublisher.PublishAsync(new UserUpdatedEvent(before, user), cancellationToken);
    }

    /// <summary>按 Id 获取用户，不存在时抛出业务异常。</summary>
    private async Task<SysUser> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        return await userRepository.GetAsync([it => it.Id == id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, $"用户 {id} 不存在");
    }

    /// <summary>内置超管账号保护：admin 拒绝编辑与状态变更。</summary>
    private static void EnsureNotAdmin(SysUser user)
    {
        if (user.UserName == AdminUserName)
        {
            throw new BusinessException(ErrorCodes.UserAdminProtected, "内置超管账号 admin 不允许编辑");
        }
    }
}

/// <summary>分配用户角色命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UpdateUserRolesCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IDomainEventPublisher domainEventPublisher,
    IUnitOfWork unitOfWork) : CommandHandler<UpdateUserRolesCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateUserRolesCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, $"用户 {command.Id} 不存在");
        await UserRoleSupport.ValidateRolesAsync(roleRepository, command.Request.RoleIds, cancellationToken);

        // 变更前查询既有角色 Id 集合（全量替换在事务内执行）
        var beforeRoleIds = (await userRoleRepository.GetListAsync([ur => ur.UserId == command.Id], cancellationToken: cancellationToken))
            .Select(ur => ur.RoleId)
            .ToList();

        await unitOfWork.RunInTransactionAsync(async () =>
        {
            await UserRoleSupport.ReplaceUserRolesAsync(userRoleRepository, command.Id, command.Request.RoleIds, cancellationToken);
        }, cancellationToken);

        // 关键节点发布用户角色重分配领域事件（Domain 为用户，携带变更前后角色 Id 集合）
        await domainEventPublisher.PublishAsync(
            new UserRolesUpdatedEvent(user, beforeRoleIds, command.Request.RoleIds), cancellationToken);
    }
}

/// <summary>修改用户状态命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UpdateUserStatusCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IDomainEventPublisher domainEventPublisher) : CommandHandler<UpdateUserStatusCommand>
{
    /// <summary>内置超管账号用户名。</summary>
    private const string AdminUserName = "admin";

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateUserStatusCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, $"用户 {command.Id} 不存在");
        if (user.UserName == AdminUserName)
        {
            throw new BusinessException(ErrorCodes.UserAdminProtected, "内置超管账号 admin 不允许编辑");
        }

        if (command.Request.Status is not (CommonStatus.Enabled or CommonStatus.Disabled))
        {
            throw new BusinessException(ErrorCodes.CommonInvalidRequest, "状态取值不合法：仅支持 1 启用 / 0 停用");
        }

        // 变更前捕获用户状态快照（状态字段即将被原位修改）
        var before = user.Snapshot<SysUser>();

        user.Status = command.Request.Status;
        await userRepository.UpdateAsync(user, cancellationToken);

        // 关键节点发布用户修改领域事件（携带变更前后用户状态，状态变化可由监听器比对 OldEntity/After）
        await domainEventPublisher.PublishAsync(new UserUpdatedEvent(before, user), cancellationToken);
    }
}
