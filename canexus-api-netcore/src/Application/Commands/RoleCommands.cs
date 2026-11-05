
using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Roles;
using AutoMapper;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Commands;
/// <summary>角色命令的共享业务逻辑（roleKey 可用性校验，供创建与更新处理器复用）。</summary>
internal static class RoleSupport
{
    /// <summary>校验 roleKey 可用：保留值由聚合守卫拦截，重复性由仓储查询校验。</summary>
    public static async Task EnsureRoleKeyAvailableAsync(IAggregateRepository<SysRole> roleRepository, string roleKey, Guid? excludeId, CancellationToken cancellationToken)
    {
        SysRole.EnsureRoleKeyNotReserved(roleKey);

        var duplicated = (await roleRepository.GetListAsync([r => r.RoleKey == roleKey], cancellationToken: cancellationToken))
            .Any(r => r.Id != excludeId);
        if (duplicated)
        {
            throw new BusinessException(ErrorCodes.RoleKeyDuplicate, $"角色权限字符「{roleKey}」已存在");
        }
    }
}

/// <summary>角色-菜单授权的共享业务逻辑（菜单存在性校验与全量替换，供角色命令处理器复用）。</summary>
internal static class RoleMenuSupport
{
    /// <summary>校验授权菜单 Id 集合全部存在。</summary>
    public static async Task ValidateMenusAsync(IAggregateRepository<SysMenu> menuRepository, List<Guid> menuIds, CancellationToken cancellationToken)
    {
        var distinct = menuIds.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return;
        }

        var count = await menuRepository.CountAsync([m => distinct.Contains(m.Id)], cancellationToken);
        if (count < distinct.Count)
        {
            throw new BusinessException(ErrorCodes.CommonDataNotFound, "所选菜单不存在或已被删除");
        }
    }

    /// <summary>全量替换角色-菜单授权（先删后插，调用方需保证工作单元事务内执行）。</summary>
    public static async Task ReplaceRoleMenusAsync(IDomainRepository<SysRoleMenu> roleMenuRepository, Guid roleId, List<Guid> menuIds, CancellationToken cancellationToken)
    {
        // 按条件物理删除旧授权（单条 SQL），再插入新授权集合
        await roleMenuRepository.DeleteAsync(rm => rm.RoleId == roleId, cancellationToken);

        foreach (var menuId in menuIds.Distinct())
        {
            await roleMenuRepository.InsertAsync(new SysRoleMenu { RoleId = roleId, MenuId = menuId }, cancellationToken);
        }
    }
}

/// <summary>创建角色命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class RoleCreateCommandHandler(
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysRoleMenu> roleMenuRepository,
    IAggregateRepository<SysMenu> menuRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : RequestHandler<RoleCreateCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(RoleCreateCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var roleKey = request.RoleKey.Trim();
        await RoleSupport.EnsureRoleKeyAvailableAsync(roleRepository, roleKey, excludeId: null, cancellationToken);
        await RoleMenuSupport.ValidateMenusAsync(menuRepository, request.MenuIds, cancellationToken);

        var role = mapper.Map<SysRole>(request);
        _ = await roleRepository.InsertAsync(role, cancellationToken);
        await RoleMenuSupport.ReplaceRoleMenusAsync(roleMenuRepository, role.Id, request.MenuIds, cancellationToken);

        // 关键节点发布角色创建领域事件（携带创建后的角色状态）
        await domainEventPublisher.PublishAsync(new RoleCreatedEvent(role), cancellationToken);
        return true;
    }
}

/// <summary>删除角色命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class RoleDeleteCommandHandler(
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IDomainRepository<SysRoleMenu> roleMenuRepository,
    IDomainEventPublisher domainEventPublisher) : RequestHandler<RoleDeleteCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(RoleDeleteCommand command, CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.RoleNotFound, $"角色 {command.Id} 不存在");
        role.EnsureNotSuperAdmin("删除");

        if (await userRoleRepository.CountAsync([ur => ur.RoleId == command.Id], cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.RoleInUse, $"角色「{role.RoleName}」已被用户引用，不允许删除");
        }

        // 变更前捕获角色状态快照（软删除可能回填墓碑字段）
        var snapshot = role.Snapshot<SysRole>();

        await roleRepository.DeleteAsync(command.Id, cancellationToken);
        await roleMenuRepository.DeleteAsync(rm => rm.RoleId == command.Id, cancellationToken);

        // 关键节点发布角色删除领域事件（携带删除前的角色状态）
        await domainEventPublisher.PublishAsync(new RoleDeletedEvent(snapshot), cancellationToken);
        return true;
    }
}

/// <summary>恢复角色命令处理器（解除软删除墓碑，幂等执行；权限缓存由监听器全量失效）。</summary>
/// <remarks>构造函数。</remarks>
/// <para>说明：删除时角色-菜单授权已物理清除，恢复仅还原角色聚合本身，菜单授权需重新分配。</para>
public sealed class RoleRestoreCommandHandler(
    IAggregateRepository<SysRole> roleRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateRestoreCommandHandler<RoleRestoreCommand, SysRole>(roleRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysRole restored)
    {
        return new RoleRestoredEvent(restored);
    }
}

/// <summary>更新角色命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class RoleUpdateCommandHandler(
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysRoleMenu> roleMenuRepository,
    IAggregateRepository<SysMenu> menuRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : RequestHandler<RoleUpdateCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(RoleUpdateCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var role = await roleRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.RoleNotFound, $"角色 {command.Id} 不存在");
        role.EnsureNotSuperAdmin("编辑");

        var roleKey = request.RoleKey.Trim();
        await RoleSupport.EnsureRoleKeyAvailableAsync(roleRepository, roleKey, excludeId: command.Id, cancellationToken);
        await RoleMenuSupport.ValidateMenusAsync(menuRepository, request.MenuIds, cancellationToken);

        // 变更前捕获角色状态快照（实体随后被原位修改）
        var before = role.Snapshot<SysRole>();

        mapper.Map(request, role);
        role.ApplyVersion(command.Version);
        await roleRepository.UpdateAsync(role, cancellationToken);
        await RoleMenuSupport.ReplaceRoleMenusAsync(roleMenuRepository, command.Id, request.MenuIds, cancellationToken);

        // 关键节点发布角色修改领域事件（携带变更前后角色状态：key / 状态 / 数据范围 / 授权菜单变化可由监听器比对）
        await domainEventPublisher.PublishAsync(new RoleUpdatedEvent(before, role), cancellationToken);
        return true;
    }
}

/// <summary>修改角色状态命令处理器（独立启停流程：super_admin 保护 → 状态合法性校验 → 乐观锁校验 → 原位更新 → 发布修改事件）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class RoleUpdateStatusCommandHandler(
    IAggregateRepository<SysRole> roleRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateUpdateCommandHandler<RoleUpdateStatusCommand, RoleStatusRequest, SysRole>(roleRepository, domainEventPublisher)
{
    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.RoleNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "角色";

    /// <inheritdoc />
    protected override Task ValidateAsync(RoleStatusRequest request, SysRole entity, CancellationToken cancellationToken)
    {
        entity.EnsureNotSuperAdmin("启停");

        if (request.Status is not (CommonStatus.Enabled or CommonStatus.Disabled))
        {
            throw new BusinessException(ErrorCodes.CommonInvalidRequest, "状态取值不合法：仅支持 1 启用 / 0 停用");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void MapToEntity(RoleStatusRequest request, SysRole entity)
    {
        entity.Status = request.Status;
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysRole before, SysRole after)
    {
        // 关键节点发布角色修改领域事件（停用即时生效：PermissionCacheInvalidationHandler 全量失效权限缓存）
        return new RoleUpdatedEvent(before, after);
    }
}
