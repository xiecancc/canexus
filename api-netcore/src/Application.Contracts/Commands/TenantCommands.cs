namespace Application.Contracts.Commands;

using Application.Contracts.Abstractions;
using Application.Contracts.Tenants;

/// <summary>创建租户命令（tenantKey 全局唯一）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record CreateTenantCommand(TenantCreateRequest Request) : ICommand<Guid>;

/// <summary>删除租户命令（存在归属用户时拒绝删除）。</summary>
/// <param name="Id">租户 Id。</param>
public sealed record DeleteTenantCommand(Guid Id) : ICommand;

/// <summary>更新租户命令（tenantKey 不可变更）。</summary>
/// <param name="Id">租户 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record UpdateTenantCommand(Guid Id, TenantUpdateRequest Request) : ICommand;
