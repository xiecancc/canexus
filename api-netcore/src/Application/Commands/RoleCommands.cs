namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Roles;
using Application.Permissions;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
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
public sealed class CreateRoleCommandHandler : CommandHandler<CreateRoleCommand, Guid>
{
    private readonly IAggregateRepository<SysRole> _roleRepository;
    private readonly IDomainRepository<SysRoleMenu> _roleMenuRepository;
    private readonly IAggregateRepository<SysMenu> _menuRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    public CreateRoleCommandHandler(
        IAggregateRepository<SysRole> roleRepository,
        IDomainRepository<SysRoleMenu> roleMenuRepository,
        IAggregateRepository<SysMenu> menuRepository,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _roleRepository = roleRepository;
        _roleMenuRepository = roleMenuRepository;
        _menuRepository = menuRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var roleKey = request.RoleKey.Trim();
        await EnsureRoleKeyAvailableAsync(roleKey, excludeId: null, cancellationToken);
        await RoleMenuSupport.ValidateMenusAsync(_menuRepository, request.MenuIds, cancellationToken);

        return await _unitOfWork.RunInTransactionAsync(async () =>
        {
            var role = _mapper.Map<SysRole>(request);
            var inserted = await _roleRepository.InsertAsync(role, cancellationToken);
            await RoleMenuSupport.ReplaceRoleMenusAsync(_roleMenuRepository, inserted.Id, request.MenuIds, cancellationToken);
            return inserted.Id;
        }, cancellationToken);
    }

    /// <summary>校验 roleKey 可用：不可使用保留值 super_admin，且不与其他角色重复。</summary>
    private async Task EnsureRoleKeyAvailableAsync(string roleKey, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (roleKey == "super_admin")
        {
            throw new BusinessException(ErrorCodes.RoleKeyReserved, "角色权限字符 super_admin 为内置保留值，不可使用");
        }

        var duplicated = (await _roleRepository.GetListAsync([r => r.RoleKey == roleKey], cancellationToken: cancellationToken))
            .Any(r => r.Id != excludeId);
        if (duplicated)
        {
            throw new BusinessException(ErrorCodes.RoleKeyDuplicate, $"角色权限字符「{roleKey}」已存在");
        }
    }

}

/// <summary>删除角色命令处理器。</summary>
public sealed class DeleteRoleCommandHandler : CommandHandler<DeleteRoleCommand>
{
    /// <summary>内置超管角色权限字符（保留值）。</summary>
    private const string SuperAdminRoleKey = "super_admin";

    private readonly IAggregateRepository<SysRole> _roleRepository;
    private readonly IDomainRepository<SysUserRole> _userRoleRepository;
    private readonly IDomainRepository<SysRoleMenu> _roleMenuRepository;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>构造函数。</summary>
    public DeleteRoleCommandHandler(
        IAggregateRepository<SysRole> roleRepository,
        IDomainRepository<SysUserRole> userRoleRepository,
        IDomainRepository<SysRoleMenu> roleMenuRepository,
        IPermissionChecker permissionChecker,
        IUnitOfWork unitOfWork)
    {
        _roleRepository = roleRepository;
        _userRoleRepository = userRoleRepository;
        _roleMenuRepository = roleMenuRepository;
        _permissionChecker = permissionChecker;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.RoleNotFound, $"角色 {command.Id} 不存在");
        if (role.RoleKey == SuperAdminRoleKey)
        {
            throw new BusinessException(ErrorCodes.RoleProtected, "内置超管角色 super_admin 不允许删除");
        }

        if (await _userRoleRepository.CountAsync(ur => ur.RoleId == command.Id, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.RoleInUse, $"角色「{role.RoleName}」已被用户引用，不允许删除");
        }

        await _unitOfWork.RunInTransactionAsync(async () =>
        {
            await _roleRepository.DeleteAsync(role, cancellationToken);
            var relations = await _roleMenuRepository.GetListAsync([rm => rm.RoleId == command.Id], cancellationToken: cancellationToken);
            foreach (var relation in relations)
            {
                await _roleMenuRepository.DeleteAsync(relation, cancellationToken);
            }
        }, cancellationToken);

        await _permissionChecker.InvalidateAllAsync();
    }
}

/// <summary>更新角色命令处理器。</summary>
public sealed class UpdateRoleCommandHandler : CommandHandler<UpdateRoleCommand>
{
    /// <summary>内置超管角色权限字符（保留值）。</summary>
    private const string SuperAdminRoleKey = "super_admin";

    private readonly IAggregateRepository<SysRole> _roleRepository;
    private readonly IDomainRepository<SysRoleMenu> _roleMenuRepository;
    private readonly IAggregateRepository<SysMenu> _menuRepository;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    /// <summary>构造函数。</summary>
    public UpdateRoleCommandHandler(
        IAggregateRepository<SysRole> roleRepository,
        IDomainRepository<SysRoleMenu> roleMenuRepository,
        IAggregateRepository<SysMenu> menuRepository,
        IPermissionChecker permissionChecker,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _roleRepository = roleRepository;
        _roleMenuRepository = roleMenuRepository;
        _menuRepository = menuRepository;
        _permissionChecker = permissionChecker;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var role = await _roleRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.RoleNotFound, $"角色 {command.Id} 不存在");
        if (role.RoleKey == SuperAdminRoleKey)
        {
            throw new BusinessException(ErrorCodes.RoleProtected, "内置超管角色 super_admin 不允许编辑");
        }

        var roleKey = request.RoleKey.Trim();
        await EnsureRoleKeyAvailableAsync(roleKey, excludeId: command.Id, cancellationToken);
        await RoleMenuSupport.ValidateMenusAsync(_menuRepository, request.MenuIds, cancellationToken);

        await _unitOfWork.RunInTransactionAsync(async () =>
        {
            _mapper.Map(request, role);
            await _roleRepository.UpdateAsync(role, cancellationToken);
            await RoleMenuSupport.ReplaceRoleMenusAsync(_roleMenuRepository, command.Id, request.MenuIds, cancellationToken);
        }, cancellationToken);

        // 角色 key / 状态 / 数据范围 / 授权菜单均可能影响已持有该角色用户的权限：全量失效
        await _permissionChecker.InvalidateAllAsync();
    }

    /// <summary>校验 roleKey 可用：不可使用保留值 super_admin，且不与其他角色重复。</summary>
    private async Task EnsureRoleKeyAvailableAsync(string roleKey, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (roleKey == SuperAdminRoleKey)
        {
            throw new BusinessException(ErrorCodes.RoleKeyReserved, "角色权限字符 super_admin 为内置保留值，不可使用");
        }

        var duplicated = (await _roleRepository.GetListAsync([r => r.RoleKey == roleKey], cancellationToken: cancellationToken))
            .Any(r => r.Id != excludeId);
        if (duplicated)
        {
            throw new BusinessException(ErrorCodes.RoleKeyDuplicate, $"角色权限字符「{roleKey}」已存在");
        }
    }

}
