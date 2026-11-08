
using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Users;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Auth;
using Domain.Services.Events;
using Domain.Services.Rules;
using Domain.Services.Users;
using Domain.Shared.Constants;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Tenants;

namespace Application.Commands;
/// <summary>用户初始密码解析：把「参数配置」中的 sys.user.initPassword 作为创建 / 导入用户的唯一缺省口令来源。</summary>
internal static class UserPasswordSupport
{
    /// <summary>读取用户初始密码（经参数值缓存；配置缺失或为空时要求调用方显式提供密码）。</summary>
    /// <param name="configRepository">参数配置仓储（只读）。</param>
    /// <param name="cache">业务缓存端口。</param>
    /// <param name="tenantId">当前租户 Id（缓存键按租户隔离，null 表示平台范围）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用户初始密码明文。</returns>
    public static async Task<string> ResolveInitialPasswordAsync(
        IDomainRepository<SysConfig> configRepository,
        ICache cache,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var configKey = ConfigConstants.InitPasswordKey;
        var cacheKey = ConfigRules.ValueCacheKey(configKey, tenantId);
        var cached = await cache.GetAsync<string>(cacheKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var config = (await configRepository.GetListAsync(
            [c => c.ConfigKey == configKey], cancellationToken: cancellationToken)).FirstOrDefault();
        if (config is null || string.IsNullOrWhiteSpace(config.ConfigValue))
        {
            throw new BusinessException(ErrorCodes.CommonValidationFailed,
                "未配置用户初始密码，请显式填写密码或在「参数配置」中设置 sys.user.initPassword");
        }

        // 仅缓存非空值：配置缺失时抛出的业务异常不进入缓存，补配后立即生效
        await cache.SetAsync(cacheKey, config.ConfigValue, CacheTtl.Medium, cancellationToken);
        return config.ConfigValue;
    }
}
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

    /// <summary>全量替换用户-角色关联（批量删除 + 批量插入，须在事务内执行——由事务管道 TransactionBehavior 保证）。</summary>
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
    IDomainRepository<SysConfig> configRepository,
    IPasswordHasher passwordHasher,
    ICache cache,
    IDomainEventPublisher domainEventPublisher,
    ITenantContext tenantContext) : RequestHandler<UserCreateCommand, bool>
{
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

        // 密码缺省取「参数配置」sys.user.initPassword（而非代码常量），使用初始密码时强制首登改密
        var useInitialPassword = string.IsNullOrWhiteSpace(request.Password);
        var password = useInitialPassword
            ? await UserPasswordSupport.ResolveInitialPasswordAsync(configRepository, cache, tenantContext.TenantId, cancellationToken)
            : request.Password!;

        var user = new SysUser
        {
            UserName = userName,
            NickName = request.NickName.Trim(),
            PasswordHash = passwordHasher.Hash(password),
            MustChangePassword = useInitialPassword,
            DeptId = request.DeptId,
            Email = request.Email,
            Phone = request.Phone,
            Status = request.Status,
            Description = request.Description
        };
        _ = await userRepository.InsertAsync(user, cancellationToken);
        await UserRoleSupport.ReplaceUserRolesAsync(userRoleRepository, user.Id, request.RoleIds, cancellationToken);

        // 关键节点发布用户创建领域事件（携带创建后的用户状态）
        await domainEventPublisher.PublishAsync(new UserCreatedEvent(user), cancellationToken);
        return true;
    }
}

/// <summary>删除用户命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UserDeleteCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IDomainEventPublisher domainEventPublisher,
    IUserContext currentUser) : RequestHandler<UserDeleteCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(UserDeleteCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, $"用户 {command.Id} 不存在");
        user.EnsureNotBuiltInAdmin("删除");

        if (currentUser.UserId == command.Id)
        {
            throw new BusinessException(ErrorCodes.UserDeleteSelfForbidden, "不能删除当前登录账号自己");
        }

        // 变更前捕获用户状态快照（软删除可能回填墓碑字段）
        var snapshot = user.Snapshot<SysUser>();

        // 软删除用户与其角色关联（保持可追溯）；关联表按条件物理删除（单条 SQL）
        await userRepository.DeleteAsync(command.Id, cancellationToken, command.Version);
        await userRoleRepository.DeleteAsync(ur => ur.UserId == command.Id, cancellationToken);

        // 关键节点发布用户删除领域事件（携带删除前的用户状态）
        await domainEventPublisher.PublishAsync(new UserDeletedEvent(snapshot), cancellationToken);
        return true;
    }
}

/// <summary>恢复用户命令处理器（解除软删除墓碑，幂等执行；权限缓存由监听器失效）。</summary>
/// <remarks>构造函数。</remarks>
/// <para>说明：删除时用户-角色关联已物理清除，恢复仅还原用户聚合本身，角色需重新分配。</para>
public sealed class UserRestoreCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateRestoreCommandHandler<UserRestoreCommand, SysUser>(userRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysUser restored)
    {
        return new UserRestoredEvent(restored);
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
        // 管理员重置密码后，用户下次登录须强制改密
        entity.MustChangePassword = true;
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
    IDomainEventPublisher domainEventPublisher) : RequestHandler<UserUpdateCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(UserUpdateCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var user = await GetOrThrowAsync(command.Id, cancellationToken);
        user.EnsureNotBuiltInAdmin("编辑");

        if (request.DeptId is not null && await deptRepository.CountAsync([d => d.Id == request.DeptId], cancellationToken) == 0)
        {
            throw new BusinessException(ErrorCodes.CommonDataNotFound, "所属部门不存在");
        }

        // 变更前捕获用户状态快照（实体随后被原位修改）
        var before = user.Snapshot<SysUser>();

        user.NickName = request.NickName.Trim();
        user.DeptId = request.DeptId;
        user.Email = request.Email;
        user.Phone = request.Phone;
        user.Status = request.Status;
        user.Description = request.Description;
        user.ApplyVersion(command.Version);
        await userRepository.UpdateAsync(user, cancellationToken);
        await UserRoleSupport.ReplaceUserRolesAsync(userRoleRepository, command.Id, request.RoleIds, cancellationToken);

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
}

/// <summary>分配用户角色命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UserUpdateRolesCommandHandler(
    IAggregateRepository<SysUser> userRepository,
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IDomainEventPublisher domainEventPublisher) : RequestHandler<UserUpdateRolesCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(UserUpdateRolesCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, $"用户 {command.Id} 不存在");

        // 角色全量替换同样以命令携带的 Version 做 CAS：经聚合仓储 OptLock 更新轮换版本令牌，
        // 陈旧版本直接抛 40005，且旧快照在替换关联表之后也无法再覆盖新状态（与替换同处业务事务）。
        user.ApplyVersion(command.Version);
        await userRepository.UpdateAsync(user, cancellationToken);
        await UserRoleSupport.ValidateRolesAsync(roleRepository, command.Request.RoleIds, cancellationToken);

        // 变更前查询既有角色 Id 集合（全量替换随后执行）
        var beforeRoleIds = (await userRoleRepository.GetListAsync([ur => ur.UserId == command.Id], cancellationToken: cancellationToken))
            .Select(ur => ur.RoleId)
            .ToList();

        await UserRoleSupport.ReplaceUserRolesAsync(userRoleRepository, command.Id, command.Request.RoleIds, cancellationToken);

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
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.UserNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "用户";

    /// <inheritdoc />
    protected override Task ValidateAsync(UserStatusRequest request, SysUser entity, CancellationToken cancellationToken)
    {
        entity.EnsureNotBuiltInAdmin("启停");

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
