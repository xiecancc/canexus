
using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Menus;
using AutoMapper;
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Events;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Application.Commands;
/// <summary>创建菜单命令处理器（标准创建流程：父菜单校验 → 映射 → 插入 → 发布创建事件）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="menuRepository">菜单树形仓储。</param>
/// <param name="domainEventPublisher">领域事件发布器。</param>
/// <param name="mapper">AutoMapper 映射器。</param>
public sealed class MenuCreateCommandHandler(
    IAggregateTreeRepository<SysMenu> menuRepository,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : AggregateCreateCommandHandler<MenuCreateCommand, MenuCreateRequest, SysMenu>(menuRepository, domainEventPublisher)
{
    /// <summary>菜单树形仓储（父菜单存在性校验）。</summary>
    private IAggregateTreeRepository<SysMenu> MenuRepository { get; } = menuRepository;

    /// <summary>AutoMapper 映射器。</summary>
    private IMapper Mapper { get; } = mapper;

    /// <inheritdoc />
    protected override async Task ValidateAsync(MenuCreateRequest request, CancellationToken cancellationToken)
    {
        if (request.ParentId != SysMenu.RootParentId)
        {
            _ = await (MenuRepository.GetAsync([it => it.Id == request.ParentId], cancellationToken)
                ?? throw new BusinessException(ErrorCodes.MenuNotFound, $"菜单 {request.ParentId} 不存在"));
        }
    }

    /// <inheritdoc />
    protected override SysMenu MapToEntity(MenuCreateRequest request)
    {
        return Mapper.Map<SysMenu>(request);
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysMenu inserted)
    {
        // 关键节点发布菜单创建领域事件（主键与审计字段已就地填充到传入实体，携带创建后的菜单状态，
        // 新菜单可能携带 perms 并被分配角色）
        return new MenuCreatedEvent(inserted);
    }
}

/// <summary>删除菜单命令处理器（标准删除流程：子菜单 / 角色引用守卫 → 乐观锁校验 → 软删除 → 发布删除事件）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="menuRepository">菜单树形仓储。</param>
/// <param name="roleMenuRepository">角色-菜单关联仓储（引用占用检查）。</param>
/// <param name="domainEventPublisher">领域事件发布器。</param>
public sealed class MenuDeleteCommandHandler(
    IAggregateTreeRepository<SysMenu> menuRepository,
    IDomainRepository<SysRoleMenu> roleMenuRepository,
    IDomainEventPublisher domainEventPublisher) : AggregateDeleteCommandHandler<MenuDeleteCommand, SysMenu>(menuRepository, domainEventPublisher)
{
    /// <summary>菜单树形仓储（子菜单检查）。</summary>
    private IAggregateTreeRepository<SysMenu> MenuRepository { get; } = menuRepository;

    /// <summary>角色-菜单关联仓储（引用占用检查）。</summary>
    private IDomainRepository<SysRoleMenu> RoleMenuRepository { get; } = roleMenuRepository;

    /// <inheritdoc />
    protected override int NotFoundErrorCode => ErrorCodes.MenuNotFound;

    /// <inheritdoc />
    protected override string EntityDisplayName => "菜单";

    /// <inheritdoc />
    protected override async Task ValidateBeforeDeleteAsync(SysMenu entity, CancellationToken cancellationToken)
    {
        if (await Repository.CountAsync([m => m.ParentId == entity.Id], cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.MenuHasChildren, $"菜单「{entity.MenuName}」下存在子菜单，不允许删除");
        }

        if (await RoleMenuRepository.CountAsync([rm => rm.MenuId == entity.Id], cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.MenuInUse, $"菜单「{entity.MenuName}」已被角色引用，不允许删除");
        }
    }

    /// <inheritdoc />
    protected override DomainEvent CreateEvent(SysMenu deleted)
    {
        // 关键节点发布菜单删除领域事件（携带删除前的菜单状态）
        return new MenuDeletedEvent(deleted);
    }
}

/// <summary>更新菜单命令处理器。</summary>
/// <remarks>构造函数。</remarks>
public sealed class MenuUpdateCommandHandler(
    IAggregateTreeRepository<SysMenu> menuRepository,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher domainEventPublisher,
    IMapper mapper) : CommandHandler<MenuUpdateCommand, bool>
{
    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(MenuUpdateCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var menu = await menuRepository.GetAsync([it => it.Id == command.Id], cancellationToken)
            ?? throw new BusinessException(ErrorCodes.MenuNotFound, $"菜单 {command.Id} 不存在");

        // 乐观锁校验：比对客户端回传版本（父级合法性由 MoveAsync 环检测兜底）
        EnsureVersionMatches(menu, request.Version);

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
        return true;
    }
}
