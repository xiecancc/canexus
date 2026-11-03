
using Domain.Entities;

namespace Domain.Events;
/// <summary>用户创建领域事件：用户创建成功后发布，Domain 为创建后的用户状态。</summary>
/// <param name="entity">创建后的用户。</param>
public sealed class UserCreatedEvent(SysUser entity) : AggregateCreatedEvent<SysUser>(entity);

/// <summary>用户修改领域事件：用户更新（含字段修改、状态调整）成功后发布，Domain 为变更后状态、OldEntity 为变更前快照。</summary>
/// <param name="before">变更前的用户快照。</param>
/// <param name="after">变更后的用户。</param>
public sealed class UserUpdatedEvent(SysUser before, SysUser after) : AggregateUpdatedEvent<SysUser>(before, after);

/// <summary>用户角色重分配领域事件：用户角色集合全量替换成功后发布，Domain 为用户、并携带变更前后角色 Id 集合。</summary>
/// <param name="user">角色被重分配的用户。</param>
/// <param name="beforeRoleIds">变更前的角色 Id 集合。</param>
/// <param name="afterRoleIds">变更后的角色 Id 集合。</param>
public sealed class UserRolesUpdatedEvent(SysUser user, IReadOnlyList<Guid> beforeRoleIds, IReadOnlyList<Guid> afterRoleIds)
    : DomainEvent<SysUser>(user)
{
    /// <summary>变更前的角色 Id 集合。</summary>
    public IReadOnlyList<Guid> BeforeRoleIds { get; } = beforeRoleIds;

    /// <summary>变更后的角色 Id 集合。</summary>
    public IReadOnlyList<Guid> AfterRoleIds { get; } = afterRoleIds;
}

/// <summary>用户重置密码领域事件：密码重置成功后发布。</summary>
/// <para>安全约定：密码哈希属敏感数据，事件不携带实体状态；监听器（如强制下线）仅按用户 Id 响应。</para>
/// <param name="userId">重置密码的用户 Id。</param>
public sealed class UserPasswordResetEvent(Guid userId) : DomainEvent
{
    /// <summary>重置密码的用户 Id。</summary>
    public Guid UserId { get; } = userId;
}

/// <summary>用户删除领域事件：用户删除成功后发布，Domain 为删除前的用户状态。</summary>
/// <para>安全约定：事件携带的实体状态含密码哈希，监听器不得将其写入日志或外发系统。</para>
/// <param name="entity">删除前的用户。</param>
public sealed class UserDeletedEvent(SysUser entity) : AggregateDeletedEvent<SysUser>(entity);

/// <summary>用户恢复领域事件：用户恢复（解除软删除墓碑）成功后发布，Domain 为恢复后的状态。</summary>
/// <param name="entity">恢复后的用户。</param>
public sealed class UserRestoredEvent(SysUser entity) : AggregateRestoredEvent<SysUser>(entity);
