
using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Menus;
using AutoMapper;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Commands;
/// <summary>创建菜单命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class CreateMenuCommandHandler(
    IAggregateTreeRepository<SysMenu> menuRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : CommandHandler<CreateMenuCommand, Guid>
{
    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateMenuCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (request.ParentId != SysMenu.RootParentId)
        {
            _ = await (menuRepository.GetAsync([it => it.Id == request.ParentId], cancellationToken)
                ?? throw new BusinessException(ErrorCodes.MenuNotFound, $"菜单 {request.ParentId} 不存在"));
        }

        var menu = mapper.Map<SysMenu>(request);
        var inserted = await menuRepository.InsertAsync(menu, cancellationToken);

        // 关键节点发布菜单创建领域事件（携带创建后的菜单状态，新菜单可能携带 perms 并被分配角色）
        await domainEventPublisher.PublishAsync(new MenuCreatedEvent(inserted), cancellationToken);
        return inserted.Id;
    }
}

/// <summary>删除菜单命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class DeleteMenuCommandHandler(
    IAggregateTreeRepository<SysMenu> menuRepository,
    IDomainRepository<SysRoleMenu> roleMenuRepository,
    IDomainEventPublisher domainEventPublisher) : CommandHandler<DeleteMenuCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteMenuCommand command, CancellationToken cancellationToken)
    {
        var menu = await menuRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.MenuNotFound, $"菜单 {command.Id} 不存在");

        if (await menuRepository.CountAsync([m => m.ParentId == command.Id], cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.MenuHasChildren, $"菜单「{menu.MenuName}」下存在子菜单，不允许删除");
        }

        if (await roleMenuRepository.CountAsync([rm => rm.MenuId == command.Id], cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.MenuInUse, $"菜单「{menu.MenuName}」已被角色引用，不允许删除");
        }

        // 变更前捕获菜单状态快照（软删除可能回填墓碑字段）
        var snapshot = menu.Snapshot<SysMenu>();

        await menuRepository.DeleteAsync(command.Id, cancellationToken);

        // 关键节点发布菜单删除领域事件（携带删除前的菜单状态）
        await domainEventPublisher.PublishAsync(new MenuDeletedEvent(snapshot), cancellationToken);
    }
}

/// <summary>更新菜单命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class UpdateMenuCommandHandler(
    IAggregateTreeRepository<SysMenu> menuRepository,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : CommandHandler<UpdateMenuCommand>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateMenuCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var menu = await menuRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.MenuNotFound, $"菜单 {command.Id} 不存在");

        var parentChanged = menu.ParentId != request.ParentId;
        var oldParentId = menu.ParentId;

        // 变更前捕获菜单状态快照（实体在事务内被原位修改，挂载点变化亦可由监听器比对）
        var before = menu.Snapshot<SysMenu>();

        await unitOfWork.RunInTransactionAsync(async () =>
        {
            // 仅更新业务字段（ParentId 还原为旧值）；父级变更统一交由树形仓储 MoveAsync 处理（环检测 / 改父）
            mapper.Map(request, menu);
            menu.ParentId = oldParentId;
            await menuRepository.UpdateAsync(menu, cancellationToken);

            if (parentChanged)
            {
                // 传入菜单模块分段错误码，保持既有 API 契约稳定
                await menuRepository.MoveAsync(command.Id, request.ParentId, new TreeMoveCodes(
                    ParentInvalid: ErrorCodes.MenuParentInvalid,
                    NodeNotFound: ErrorCodes.MenuNotFound,
                    ParentNotFound: ErrorCodes.CommonDataNotFound,
                    EntityName: "菜单"), cancellationToken);
            }
        }, cancellationToken);

        // 关键节点发布菜单修改领域事件（携带变更前后菜单状态）
        await domainEventPublisher.PublishAsync(new MenuUpdatedEvent(before, menu), cancellationToken);
    }
}
