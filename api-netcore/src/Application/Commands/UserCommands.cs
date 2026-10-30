namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Users;
using Application.Permissions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using Domain.Services.Auth;
using Domain.Services.Users;

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

        var count = await roleRepository.CountAsync(r => distinct.Contains(r.Id), cancellationToken);
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
public sealed class CreateUserCommandHandler : CommandHandler<CreateUserCommand, Guid>
{
    /// <summary>内置超管账号用户名。</summary>
    public const string AdminUserName = "admin";

    /// <summary>创建用户缺省密码。</summary>
    private const string DefaultPassword = "123456";

    private readonly IAggregateRepository<SysUser> _userRepository;
    private readonly IAggregateRepository<SysRole> _roleRepository;
    private readonly IAggregateTreeRepository<SysDept> _deptRepository;
    private readonly IDomainRepository<SysUserRole> _userRoleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>构造函数。</summary>
    public CreateUserCommandHandler(
        IAggregateRepository<SysUser> userRepository,
        IAggregateRepository<SysRole> roleRepository,
        IAggregateTreeRepository<SysDept> deptRepository,
        IDomainRepository<SysUserRole> userRoleRepository,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _deptRepository = deptRepository;
        _userRoleRepository = userRoleRepository;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var userName = request.UserName.Trim();
        if (await _userRepository.CountAsync(u => u.UserName == userName, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.UserNameDuplicate, $"用户名「{userName}」已存在");
        }

        await UserRoleSupport.ValidateRolesAsync(_roleRepository, request.RoleIds, cancellationToken);
        if (request.DeptId != null && await _deptRepository.CountAsync(d => d.Id == request.DeptId, cancellationToken) == 0)
        {
            throw new BusinessException(ErrorCodes.CommonDataNotFound, "所属部门不存在");
        }

        return await _unitOfWork.RunInTransactionAsync(async () =>
        {
            var user = new SysUser
            {
                UserName = userName,
                NickName = request.NickName.Trim(),
                // 密码缺省使用默认密码 123456（DTO 注释约定），BCrypt 存储
                PasswordHash = _passwordHasher.Hash(
                    string.IsNullOrWhiteSpace(request.Password) ? DefaultPassword : request.Password),
                DeptId = request.DeptId,
                Email = request.Email,
                Phone = request.Phone,
                Status = request.Status,
                Description = request.Description
            };
            var inserted = await _userRepository.InsertAsync(user, cancellationToken);
            await UserRoleSupport.ReplaceUserRolesAsync(_userRoleRepository, inserted.Id, request.RoleIds, cancellationToken);
            return inserted.Id;
        }, cancellationToken);
    }
}

/// <summary>删除用户命令处理器。</summary>
public sealed class DeleteUserCommandHandler : CommandHandler<DeleteUserCommand>
{
    /// <summary>内置超管账号用户名。</summary>
    private const string AdminUserName = "admin";

    private readonly IAggregateRepository<SysUser> _userRepository;
    private readonly IDomainRepository<SysUserRole> _userRoleRepository;
    private readonly IPermissionChecker _permissionChecker;
    private readonly UserContext _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>构造函数。</summary>
    public DeleteUserCommandHandler(
        IAggregateRepository<SysUser> userRepository,
        IDomainRepository<SysUserRole> userRoleRepository,
        IPermissionChecker permissionChecker,
        UserContext currentUser,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _userRoleRepository = userRoleRepository;
        _permissionChecker = permissionChecker;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, $"用户 {command.Id} 不存在");
        if (user.UserName == AdminUserName)
        {
            throw new BusinessException(ErrorCodes.UserAdminProtected, "内置超管账号 admin 不允许删除");
        }

        if (_currentUser.UserId == command.Id)
        {
            throw new BusinessException(ErrorCodes.UserDeleteSelfForbidden, "不能删除当前登录账号自己");
        }

        await _unitOfWork.RunInTransactionAsync(async () =>
        {
            // 软删除用户与其角色关联（保持可追溯）
            await _userRepository.DeleteAsync(user, cancellationToken);
            var relations = await _userRoleRepository.GetListAsync([ur => ur.UserId == command.Id], cancellationToken: cancellationToken);
            foreach (var relation in relations)
            {
                await _userRoleRepository.DeleteAsync(relation, cancellationToken);
            }
        }, cancellationToken);

        await _permissionChecker.InvalidateAsync(command.Id);
    }
}

/// <summary>重置用户密码命令处理器。</summary>
public sealed class ResetUserPasswordCommandHandler : CommandHandler<ResetUserPasswordCommand>
{
    private readonly IAggregateRepository<SysUser> _userRepository;
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>构造函数。</summary>
    public ResetUserPasswordCommandHandler(
        IAggregateRepository<SysUser> userRepository,
        IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(ResetUserPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, $"用户 {command.Id} 不存在");
        user.PasswordHash = _passwordHasher.Hash(command.Request.Password);
        await _userRepository.UpdateAsync(user, cancellationToken);
    }
}

/// <summary>更新用户命令处理器。</summary>
public sealed class UpdateUserCommandHandler : CommandHandler<UpdateUserCommand>
{
    /// <summary>内置超管账号用户名。</summary>
    private const string AdminUserName = "admin";

    private readonly IAggregateRepository<SysUser> _userRepository;
    private readonly IAggregateRepository<SysRole> _roleRepository;
    private readonly IAggregateTreeRepository<SysDept> _deptRepository;
    private readonly IDomainRepository<SysUserRole> _userRoleRepository;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>构造函数。</summary>
    public UpdateUserCommandHandler(
        IAggregateRepository<SysUser> userRepository,
        IAggregateRepository<SysRole> roleRepository,
        IAggregateTreeRepository<SysDept> deptRepository,
        IDomainRepository<SysUserRole> userRoleRepository,
        IPermissionChecker permissionChecker,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _deptRepository = deptRepository;
        _userRoleRepository = userRoleRepository;
        _permissionChecker = permissionChecker;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var user = await GetOrThrowAsync(command.Id, cancellationToken);
        EnsureNotAdmin(user);

        if (request.DeptId != null && await _deptRepository.CountAsync(d => d.Id == request.DeptId, cancellationToken) == 0)
        {
            throw new BusinessException(ErrorCodes.CommonDataNotFound, "所属部门不存在");
        }

        await _unitOfWork.RunInTransactionAsync(async () =>
        {
            user.NickName = request.NickName.Trim();
            user.DeptId = request.DeptId;
            user.Email = request.Email;
            user.Phone = request.Phone;
            user.Status = request.Status;
            user.Description = request.Description;
            await _userRepository.UpdateAsync(user, cancellationToken);
            await UserRoleSupport.ReplaceUserRolesAsync(_userRoleRepository, command.Id, request.RoleIds, cancellationToken);
        }, cancellationToken);

        // 用户角色已变化：失效该用户权限缓存
        await _permissionChecker.InvalidateAsync(command.Id);
    }

    /// <summary>按 Id 获取用户，不存在时抛出业务异常。</summary>
    private async Task<SysUser> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _userRepository.GetAsync(id, cancellationToken)
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
public sealed class UpdateUserRolesCommandHandler : CommandHandler<UpdateUserRolesCommand>
{
    private readonly IAggregateRepository<SysUser> _userRepository;
    private readonly IAggregateRepository<SysRole> _roleRepository;
    private readonly IDomainRepository<SysUserRole> _userRoleRepository;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>构造函数。</summary>
    public UpdateUserRolesCommandHandler(
        IAggregateRepository<SysUser> userRepository,
        IAggregateRepository<SysRole> roleRepository,
        IDomainRepository<SysUserRole> userRoleRepository,
        IPermissionChecker permissionChecker,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _userRoleRepository = userRoleRepository;
        _permissionChecker = permissionChecker;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateUserRolesCommand command, CancellationToken cancellationToken)
    {
        _ = await (_userRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, $"用户 {command.Id} 不存在"));
        await UserRoleSupport.ValidateRolesAsync(_roleRepository, command.Request.RoleIds, cancellationToken);

        await _unitOfWork.RunInTransactionAsync(async () =>
        {
            await UserRoleSupport.ReplaceUserRolesAsync(_userRoleRepository, command.Id, command.Request.RoleIds, cancellationToken);
        }, cancellationToken);

        await _permissionChecker.InvalidateAsync(command.Id);
    }
}

/// <summary>修改用户状态命令处理器。</summary>
public sealed class UpdateUserStatusCommandHandler : CommandHandler<UpdateUserStatusCommand>
{
    /// <summary>内置超管账号用户名。</summary>
    private const string AdminUserName = "admin";

    private readonly IAggregateRepository<SysUser> _userRepository;
    private readonly IPermissionChecker _permissionChecker;

    /// <summary>构造函数。</summary>
    public UpdateUserStatusCommandHandler(
        IAggregateRepository<SysUser> userRepository,
        IPermissionChecker permissionChecker)
    {
        _userRepository = userRepository;
        _permissionChecker = permissionChecker;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateUserStatusCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.UserNotFound, $"用户 {command.Id} 不存在");
        if (user.UserName == AdminUserName)
        {
            throw new BusinessException(ErrorCodes.UserAdminProtected, "内置超管账号 admin 不允许编辑");
        }

        if (command.Request.Status is not (CommonStatus.Enabled or CommonStatus.Disabled))
        {
            throw new BusinessException(ErrorCodes.CommonInvalidRequest, "状态取值不合法：仅支持 1 启用 / 0 停用");
        }

        user.Status = command.Request.Status;
        await _userRepository.UpdateAsync(user, cancellationToken);
        await _permissionChecker.InvalidateAsync(command.Id);
    }
}
