using Application.Contracts.Abstractions;
using Application.Contracts.Posts;
using Domain.Shared.Permissions;

namespace Application.Contracts.Commands;
/// <summary>创建岗位命令（名称 / 编码必填且长度合法、编码唯一、状态 ∈ {0,1}）。</summary>
[RequireCommandPermission(PermissionDefinitions.SystemPostAdd)]
public sealed record PostCreateCommand : AggregateCreateCommand<PostInputRequest>
{
    /// <summary>构造创建岗位命令。</summary>
    /// <param name="request">创建请求。</param>
    public PostCreateCommand(PostInputRequest request) : base(request) { }
}

/// <summary>删除岗位命令（岗位仍被用户引用时拒绝删除）。</summary>
/// <param name="Id">岗位 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
[RequireCommandPermission(PermissionDefinitions.SystemPostRemove)]
public sealed record PostDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>恢复岗位命令（解除软删除墓碑；幂等执行，不存在或未删除时静默忽略）。</summary>
/// <param name="Id">岗位 Id。</param>
/// <param name="Version">乐观锁版本号（与删除相同的路由参数模式）。</param>
[RequireCommandPermission(PermissionDefinitions.SystemPostRestore)]
public sealed record PostRestoreCommand(Guid Id, Guid Version) : AggregateRestoreCommand(Id, Version);

/// <summary>更新岗位命令（校验规则与创建一致）。</summary>
[RequireCommandPermission(PermissionDefinitions.SystemPostEdit)]
public sealed record PostUpdateCommand : AggregateUpdateCommand<PostInputRequest>
{
    /// <summary>构造更新岗位命令。</summary>
    /// <param name="id">岗位 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">更新请求。</param>
    public PostUpdateCommand(Guid id, Guid version, PostInputRequest request) : base(id, version, request) { }
}
