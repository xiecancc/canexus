namespace Application.Commands;

using Application.Abstractions;
using Application.Contracts.Commands;
using Application.Contracts.Tenants;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

/// <summary>创建租户命令处理器。</summary>
public sealed class CreateTenantCommandHandler : CommandHandler<CreateTenantCommand, Guid>
{
    private readonly IAggregateRepository<SysTenant> _tenantRepository;

    /// <summary>构造函数。</summary>
    /// <param name="tenantRepository">租户仓储。</param>
    public CreateTenantCommandHandler(IAggregateRepository<SysTenant> tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    /// <inheritdoc />
    protected override async Task<Guid> HandleAsync(CreateTenantCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var tenantKey = request.TenantKey.Trim();
        var tenantName = request.TenantName.Trim();

        if (await _tenantRepository.CountAsync(t => t.TenantKey == tenantKey, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.TenantKeyDuplicate, $"租户标识「{tenantKey}」已存在");
        }

        var entity = new SysTenant
        {
            TenantKey = tenantKey,
            TenantName = tenantName,
            ContactPhone = request.ContactPhone,
            Status = request.Status,
            Description = request.Description,
            ExpireTime = request.ExpireTime
        };
        var inserted = await _tenantRepository.InsertAsync(entity, cancellationToken);
        return inserted.Id;
    }
}

/// <summary>删除租户命令处理器。</summary>
public sealed class DeleteTenantCommandHandler : CommandHandler<DeleteTenantCommand>
{
    private readonly IAggregateRepository<SysTenant> _tenantRepository;
    private readonly IAggregateRepository<SysUser> _userRepository;

    /// <summary>构造函数。</summary>
    /// <param name="tenantRepository">租户仓储。</param>
    /// <param name="userRepository">用户仓储（归属校验）。</param>
    public DeleteTenantCommandHandler(IAggregateRepository<SysTenant> tenantRepository, IAggregateRepository<SysUser> userRepository)
    {
        _tenantRepository = tenantRepository;
        _userRepository = userRepository;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(DeleteTenantCommand command, CancellationToken cancellationToken)
    {
        var entity = await _tenantRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.TenantNotFound, $"租户 {command.Id} 不存在");

        // 存在归属用户（未删除数据按租户 Id 归属）时拒绝删除
        if (await _userRepository.CountAsync(u => u.TenantId == entity.Id, cancellationToken) > 0)
        {
            throw new BusinessException(ErrorCodes.TenantInUse, $"租户「{entity.TenantName}」下存在归属用户，不允许删除");
        }

        await _tenantRepository.DeleteAsync(entity, cancellationToken);
    }
}

/// <summary>更新租户命令处理器。</summary>
public sealed class UpdateTenantCommandHandler : CommandHandler<UpdateTenantCommand>
{
    private readonly IAggregateRepository<SysTenant> _tenantRepository;

    /// <summary>构造函数。</summary>
    /// <param name="tenantRepository">租户仓储。</param>
    public UpdateTenantCommandHandler(IAggregateRepository<SysTenant> tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    /// <inheritdoc />
    protected override async Task HandleAsync(UpdateTenantCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var entity = await _tenantRepository.GetAsync(command.Id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.TenantNotFound, $"租户 {command.Id} 不存在");

        var tenantKey = request.TenantKey.Trim();
        var tenantName = request.TenantName.Trim();

        // 租户标识不可变更（与既有值不一致即拒绝，归属关系按 tenantKey 建立）
        if (!string.Equals(tenantKey, entity.TenantKey, StringComparison.Ordinal))
        {
            throw new BusinessException(ErrorCodes.TenantKeyImmutable, "租户标识（tenantKey）不可变更");
        }

        entity.TenantName = tenantName;
        entity.ContactPhone = request.ContactPhone;
        entity.Status = request.Status;
        entity.Description = request.Description;
        entity.ExpireTime = request.ExpireTime;
        await _tenantRepository.UpdateAsync(entity, cancellationToken);
    }
}
