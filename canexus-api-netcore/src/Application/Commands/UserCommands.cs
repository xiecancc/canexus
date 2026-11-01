
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
        await userRoleRepository.DeleteAsync(ur => ur.UserId == userId, cancellationToken);
        await userRoleRepository.InsertAsync(
            roleIds.Distinct().Select(roleId => new SysUserRole { UserId = userId, RoleId = roleId }),
            cancellationToken);
    }
}

/// <summary>创建用户命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UserCreateCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IAggregateRepository<SysRole> roleRepository,
    IAggregateTreeRepository<SysDept> deptRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IPasswordHasher passwordHasher,
    IDomainEventPublisher domainEventPublisher,
    IUnitOfWork unitOfWork) : CommandHandler<UserCreateCommand, bool>
{
    /// <summary>内置超管账号用户名。</summary>
    public const string AdminUserName = "admin";

    /// <summary>创建用户缺省密码。</summary>
    private const string DefaultPassword = "123456";

    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(UserCreateCommand command, CancellationToken cancellationToken)
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
            _ = await userRepository.InsertAsync(user, cancellationToken);
            await UserRoleSupport.ReplaceUserRolesAsync(userRoleRepository, user.Id, request.RoleIds, cancellationToken);
            return user;
        }, cancellationToken);

        // 关键节点发布用户创建领域事件（携带创建后的用户状态）
        await domainEventPublisher.PublishAsync(new UserCreatedEvent(inserted), cancellationToken);
        return true;
    }
}

/// <summary>删除用户命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UserDeleteCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IDomainEventPublisher domainEventPublisher,
    IUserContext currentUser,
    IUnitOfWork unitOfWork) : CommandHandler<UserDeleteCommand, bool>
{
    /// <summary>内置超管账号用户名。</summary>
    private const string AdminUserName = "admin";

    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(UserDeleteCommand command, CancellationToken cancellationToken)
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

        // 乐观锁校验：守卫通过后比对客户端回传版本
        EnsureVersionMatches(user, command.Version);

        // 变更前捕获用户状态快照（软删除可能回填墓碑字段）
        var snapshot = user.Snapshot<SysUser>();

        await unitOfWork.RunInTransactionAsync(async () =>
        {
            // 软删除用户与其角色关联（保持可追溯）；关联表按条件物理删除（单条 SQL）
            await userRepository.DeleteAsync(command.Id, cancellationToken);
            await userRoleRepository.DeleteAsync(ur => ur.UserId == command.Id, cancellationToken);
        }, cancellationToken);

        // 关键节点发布用户删除领域事件（携带删除前的用户状态）
        await domainEventPublisher.PublishAsync(new UserDeletedEvent(snapshot), cancellationToken);
        return true;
    }
}

/// <summary>重置用户密码命令处理器（标准更新流程：乐观锁校验 → BCrypt 哈希写入 → 发布重置事件）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="userRepository">用户聚合仓储。</param>
/// <param name="passwordHasher">密码哈希器（BCrypt）。</param>
/// <param name="domainEventPublisher">领域事件发布器。</param>
public sealed class UserResetPasswordCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IPasswordHasher passwordHasher,
    IDomainEventPublisher domainEventPublisher) : AggregateUpdateCommandHandler<UserResetPasswordCommand, UserPasswordRequest, SysUser>(userRepository, domainEventPublisher)
{
    /// <summary>密码哈希器（BCrypt）。</summary>
    private IPasswordHasher PasswordHasher { get; } = passwordHasher;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.UserNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "用户";

    /// <inheritdoc />
    protected override Task ValidateAsync(UserPasswordRequest request, SysUser entity, CancellationToken cancellationToken)
    {
        // 无业务守卫：密码强度由验证器拦截，乐观锁比对由基类统一执行
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void MapToEntity(UserPasswordRequest request, SysUser entity)
    {
        entity.PasswordHash = PasswordHasher.Hash(request.Password);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysUser before, SysUser after)
    {
        // 关键节点发布用户重置密码领域事件（不携带状态：密码哈希属敏感数据，强制下线等监听器按用户 Id 响应）
        return new UserPasswordResetEvent(after.Id);
    }
}

/// <summary>更新用户命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UserUpdateCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IAggregateTreeRepository<SysDept> deptRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IDomainEventPublisher domainEventPublisher,
    IUnitOfWork unitOfWork) : CommandHandler<UserUpdateCommand, bool>
{
    /// <summary>内置超管账号用户名。</summary>
    private const string AdminUserName = "admin";

    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(UserUpdateCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var user = await GetOrThrowAsync(command.Id, cancellationToken);
        EnsureNotAdmin(user);

        if (request.DeptId is not null && await deptRepository.CountAsync([d => d.Id == request.DeptId], cancellationToken) == 0)
        {
            throw new BusinessException(ErrorCodes.CommonDataNotFound, "所属部门不存在");
        }

        // 乐观锁校验：守卫通过后比对客户端回传版本
        EnsureVersionMatches(user, request.Version);

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
        return true;
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
public sealed class UserUpdateRolesCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IDomainEventPublisher domainEventPublisher,
    IUnitOfWork unitOfWork) : CommandHandler<UserUpdateRolesCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(UserUpdateRolesCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, $"用户 {command.Id} 不存在");
        await UserRoleSupport.ValidateRolesAsync(roleRepository, command.Request.RoleIds, cancellationToken);

        // 乐观锁校验：角色分配同为聚合修改操作，比对客户端回传版本
        EnsureVersionMatches(user, command.Request.Version);

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
        return true;
    }
}

/// <summary>修改用户状态命令处理器（标准更新流程：超管保护 → 状态合法性校验 → 乐观锁校验 → 原位更新 → 发布修改事件）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="userRepository">用户聚合仓储。</param>
/// <param name="domainEventPublisher">领域事件发布器。</param>
public sealed class UserUpdateStatusCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateUpdateCommandHandler<UserUpdateStatusCommand, UserStatusRequest, SysUser>(userRepository, domainEventPublisher)
{
    /// <summary>内置超管账号用户名。</summary>
    private const string AdminUserName = "admin";

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.UserNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "用户";

    /// <inheritdoc />
    protected override Task ValidateAsync(UserStatusRequest request, SysUser entity, CancellationToken cancellationToken)
    {
        if (entity.UserName == AdminUserName)
        {
            throw new BusinessException(ErrorCodes.UserAdminProtected, "内置超管账号 admin 不允许编辑");
        }

        if (request.Status is not (CommonStatus.Enabled or CommonStatus.Disabled))
        {
            throw new BusinessException(ErrorCodes.CommonInvalidRequest, "状态取值不合法：仅支持 1 启用 / 0 停用");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void MapToEntity(UserStatusRequest request, SysUser entity)
    {
        entity.Status = request.Status;
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysUser before, SysUser after)
    {
        // 关键节点发布用户修改领域事件（携带变更前后用户状态，状态变化可由监听器比对 OldEntity/After）
        return new UserUpdatedEvent(before, after);
    }
}
