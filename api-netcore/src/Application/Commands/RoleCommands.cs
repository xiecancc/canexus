namespace Application.Commands;

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

        var count = await menuRepository.CountAsync(m => distinct.Contains(m.Id), cancellationToken);
        if (count < distinct.Count)
        {
            throw new BusinessException(ErrorCodes.CommonDataNotFound, "所选菜单不存在或已被删除");
        }
    }

    /// <summary>全量替换角色-菜单授权（先删后插，调用方需保证工作单元事务内执行）。</summary>
    public static async Task ReplaceRoleMenusAsync(IDomainRepository<SysRoleMenu> roleMenuRepository, Guid roleId, List<Guid> menuIds, CancellationToken cancellationToken)
    {
        var existing = await roleMenuRepository.GetListAsync([rm => rm.RoleId == roleId], cancellationToken: cancellationToken);
        foreach (var relation in existing)
        {
            await roleMenuRepository.DeleteAsync(relation, cancellationToken);
        }

        foreach (var menuId in menuIds.Distinct())
        {
            await roleMenuRepository.InsertAsync(new SysRoleMenu { RoleId = roleId, MenuId = menuId }, cancellationToken);
        }
    }
}

/// <summary>创建角色命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class CreateRoleCommandHandler(
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysRoleMenu> roleMenuRepository,
    IAggregateRepository<SysMenu> menuRepository,
    IDomainEventPublisher domainEventPublisher,
    IUnitOfWork unitOfWork,
    IMapper mapper) : CommandHandler<CreateRoleCommand, Guid>
{
    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var roleKey = request.RoleKey.Trim();
        await EnsureRoleKeyAvailableAsync(roleKey, excludeId: null, cancellationToken);
        await RoleMenuSupport.ValidateMenusAsync(menuRepository, request.MenuIds, cancellationToken);

        var inserted = await unitOfWork.RunInTransactionAsync(async () =>
        {
            var role = mapper.Map<SysRole>(request);
            var insertedRole = await roleRepository.InsertAsync(role, cancellationToken);
            await RoleMenuSupport.ReplaceRoleMenusAsync(roleMenuRepository, insertedRole.Id, request.MenuIds, cancellationToken);
            return insertedRole;
        }, cancellationToken);

        // 关键节点发布角色创建领域事件（携带创建后的角色状态）
        await domainEventPublisher.PublishAsync(new RoleCreatedEvent(inserted), cancellationToken);
        return inserted.Id;
    }

    /// <summary>校验 roleKey 可用：不可使用保留值 super_admin，且不与其他角色重复。</summary>
    private async Task EnsureRoleKeyAvailableAsync(string roleKey, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (roleKey == "super_admin")
        {
            throw new BusinessException(ErrorCodes.RoleKeyReserved, "角色权限字符 super_admin 为内置保留值，不可使用");
        }

        var duplicated = (await roleRepository.GetListAsync([r => r.RoleKey == roleKey], cancellationToken: cancellationToken))
            .Any(r => r.Id != excludeId);
        if (duplicated)
        {
            throw new BusinessException(ErrorCodes.RoleKeyDuplicate, $"角色权限字符「{roleKey}」已存在");
        }
    }

}

/// <summary>删除角色命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DeleteRoleCommandHandler(
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysUserRole> userRoleRepository,
    IDomainRepository<SysRoleMenu> roleMenuRepository,
    IDomainEventPublisher domainEventPublisher,
    IUnitOfWork unitOfWork) : CommandHandler<DeleteRoleCommand>
{
    /// <summary>内置超管角色权限字符（保留值）。</summary>
    private const string SuperAdminRoleKey = "super_admin";

    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.RoleNotFound, $"角色 {command.Id} 不存在");
        if (role.RoleKey == SuperAdminRoleKey)
        {
            throw new BusinessException(ErrorCodes.RoleProtected, "内置超管角色 super_admin 不允许删除");
        }

        if (await userRoleRepository.CountAsync(ur => ur.RoleId == command.Id, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.RoleInUse, $"角色「{role.RoleName}」已被用户引用，不允许删除");
        }

        // 变更前捕获角色状态快照（软删除可能回填墓碑字段）
        var snapshot = role.Snapshot<SysRole>();

        await unitOfWork.RunInTransactionAsync(async () =>
        {
            await roleRepository.DeleteAsync(role, cancellationToken);
            var relations = await roleMenuRepository.GetListAsync([rm => rm.RoleId == command.Id], cancellationToken: cancellationToken);
            foreach (var relation in relations)
            {
                await roleMenuRepository.DeleteAsync(relation, cancellationToken);
            }
        }, cancellationToken);

        // 关键节点发布角色删除领域事件（携带删除前的角色状态）
        await domainEventPublisher.PublishAsync(new RoleDeletedEvent(snapshot), cancellationToken);
    }
}

/// <summary>更新角色命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UpdateRoleCommandHandler(
    IAggregateRepository<SysRole> roleRepository,
    IDomainRepository<SysRoleMenu> roleMenuRepository,
    IAggregateRepository<SysMenu> menuRepository,
    IDomainEventPublisher domainEventPublisher,
    IUnitOfWork unitOfWork,
    IMapper mapper) : CommandHandler<UpdateRoleCommand>
{
    /// <summary>内置超管角色权限字符（保留值）。</summary>
    private const string SuperAdminRoleKey = "super_admin";

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var role = await roleRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.RoleNotFound, $"角色 {command.Id} 不存在");
        if (role.RoleKey == SuperAdminRoleKey)
        {
            throw new BusinessException(ErrorCodes.RoleProtected, "内置超管角色 super_admin 不允许编辑");
        }

        var roleKey = request.RoleKey.Trim();
        await EnsureRoleKeyAvailableAsync(roleKey, excludeId: command.Id, cancellationToken);
        await RoleMenuSupport.ValidateMenusAsync(menuRepository, request.MenuIds, cancellationToken);

        // 变更前捕获角色状态快照（实体在事务内被原位修改）
        var before = role.Snapshot<SysRole>();

        await unitOfWork.RunInTransactionAsync(async () =>
        {
            mapper.Map(request, role);
            await roleRepository.UpdateAsync(role, cancellationToken);
            await RoleMenuSupport.ReplaceRoleMenusAsync(roleMenuRepository, command.Id, request.MenuIds, cancellationToken);
        }, cancellationToken);

        // 关键节点发布角色修改领域事件（携带变更前后角色状态：key / 状态 / 数据范围 / 授权菜单变化可由监听器比对）
        await domainEventPublisher.PublishAsync(new RoleUpdatedEvent(before, role), cancellationToken);
    }

    /// <summary>校验 roleKey 可用：不可使用保留值 super_admin，且不与其他角色重复。</summary>
    private async Task EnsureRoleKeyAvailableAsync(string roleKey, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (roleKey == SuperAdminRoleKey)
        {
            throw new BusinessException(ErrorCodes.RoleKeyReserved, "角色权限字符 super_admin 为内置保留值，不可使用");
        }

        var duplicated = (await roleRepository.GetListAsync([r => r.RoleKey == roleKey], cancellationToken: cancellationToken))
            .Any(r => r.Id != excludeId);
        if (duplicated)
        {
            throw new BusinessException(ErrorCodes.RoleKeyDuplicate, $"角色权限字符「{roleKey}」已存在");
        }
    }

}
