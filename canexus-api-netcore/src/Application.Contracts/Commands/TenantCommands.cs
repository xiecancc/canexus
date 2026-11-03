
using Application.Contracts.Abstractions;
using Application.Contracts.Tenants;

namespace Application.Contracts.Commands;
/// <summary>创建租户命令（tenantKey 全局唯一）。</summary>
public sealed record TenantCreateCommand : AggregateCreateCommand<TenantInputRequest>
{
    /// <summary>构造创建租户命令。</summary>
    /// <param name="request">创建请求。</param>
    public TenantCreateCommand(TenantInputRequest request) : base(request) { }
}

/// <summary>删除租户命令（存在归属用户时拒绝删除）。</summary>
/// <param name="Id">租户 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record TenantDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>恢复租户命令（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
/// <param name="Id">租户 Id。</param>
/// <param name="Version">乐观锁版本号（与删除相同的路由参数模式）。</param>
public sealed record TenantRestoreCommand(Guid Id, Guid Version) : AggregateRestoreCommand(Id, Version);

/// <summary>更新租户命令（tenantKey 不可变更）。</summary>
public sealed record TenantUpdateCommand : AggregateUpdateCommand<TenantInputRequest>
{
    /// <summary>构造更新租户命令。</summary>
    /// <param name="id">租户 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">更新请求。</param>
    public TenantUpdateCommand(Guid id, Guid version, TenantInputRequest request) : base(id, version, request) { }
}
