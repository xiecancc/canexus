
using Application.Contracts.Abstractions;
using Application.Contracts.Depts;
using Domain.Shared.Permissions;

namespace Application.Contracts.Commands;
/// <summary>创建部门命令（父部门存在性由处理器校验）。</summary>
[RequireCommandPermission(PermissionDefinitions.SystemDeptAdd)]
public sealed record DeptCreateCommand : AggregateCreateCommand<DeptInputRequest>
{
    /// <summary>构造创建部门命令。</summary>
    /// <param name="request">创建请求。</param>
    public DeptCreateCommand(DeptInputRequest request) : base(request) { }
}

/// <summary>删除部门命令（存在子部门或部门下存在用户时拒绝）。</summary>
/// <param name="Id">部门 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
[RequireCommandPermission(PermissionDefinitions.SystemDeptRemove)]
public sealed record DeptDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>恢复部门命令（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
/// <param name="Id">部门 Id。</param>
/// <param name="Version">乐观锁版本号（与删除相同的路由参数模式）。</param>
[RequireCommandPermission(PermissionDefinitions.SystemDeptRestore)]
public sealed record DeptRestoreCommand(Guid Id, Guid Version) : AggregateRestoreCommand(Id, Version);

/// <summary>更新部门命令（父级不可选自身或自己的后代）。</summary>
[RequireCommandPermission(PermissionDefinitions.SystemDeptEdit)]
public sealed record DeptUpdateCommand : AggregateUpdateCommand<DeptInputRequest>
{
    /// <summary>构造更新部门命令。</summary>
    /// <param name="id">部门 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">更新请求。</param>
    public DeptUpdateCommand(Guid id, Guid version, DeptInputRequest request) : base(id, version, request) { }
}
