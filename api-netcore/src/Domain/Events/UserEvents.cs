namespace Domain.Events;

using Domain.Entities;

/// <summary>用户创建领域事件：用户创建成功后发布，携带创建后的用户状态。</summary>
/// <param name="Entity">创建后的用户。</param>
public sealed class UserCreatedEvent(SysUser entity) : CreatedEvent<SysUser>("user.created", entity);

/// <summary>用户修改领域事件：用户更新（含字段修改、状态调整）成功后发布，携带变更前后状态。</summary>
/// <param name="Before">变更前的用户快照。</param>
/// <param name="After">变更后的用户。</param>
public sealed class UserUpdatedEvent(SysUser before, SysUser after) : UpdatedEvent<SysUser>("user.updated", before, after);

/// <summary>用户角色重分配领域事件：用户角色集合全量替换成功后发布，携带变更前后角色 Id 集合。</summary>
/// <param name="UserId">用户 Id。</param>
/// <param name="BeforeRoleIds">变更前的角色 Id 集合。</param>
/// <param name="AfterRoleIds">变更后的角色 Id 集合。</param>
public sealed class UserRolesUpdatedEvent(Guid userId, IReadOnlyList<Guid> beforeRoleIds, IReadOnlyList<Guid> afterRoleIds)
    : DomainEvent("user.roles_updated", userId)
{
    /// <summary>用户 Id。</summary>
    public Guid UserId { get; } = userId;

    /// <summary>变更前的角色 Id 集合。</summary>
    public IReadOnlyList<Guid> BeforeRoleIds { get; } = beforeRoleIds;

    /// <summary>变更后的角色 Id 集合。</summary>
    public IReadOnlyList<Guid> AfterRoleIds { get; } = afterRoleIds;
}

/// <summary>用户重置密码领域事件：密码重置成功后发布。</summary>
/// <para>安全约定：密码哈希属敏感数据，事件不携带聚合状态；监听器（如强制下线）仅按用户 Id 响应。</para>
/// <param name="UserId">重置密码的用户 Id。</param>
public sealed class UserPasswordResetEvent(Guid userId) : DomainEvent("user.password_reset", userId)
{
    /// <summary>重置密码的用户 Id。</summary>
    public Guid UserId { get; } = userId;
}

/// <summary>用户删除领域事件：用户删除成功后发布，携带删除前的用户状态。</summary>
/// <para>安全约定：事件携带的实体状态含密码哈希，监听器不得将其写入日志或外发系统。</para>
/// <param name="Entity">删除前的用户。</param>
public sealed class UserDeletedEvent(SysUser entity) : DeletedEvent<SysUser>("user.deleted", entity);
