
using Domain.Entities;

namespace Domain.Events;
/// <summary>岗位创建领域事件：岗位创建成功后发布，Domain 为创建后的状态（缓存失效等响应动作可由监听器扩展处理）。</summary>
/// <param name="newEntity">创建后的岗位。</param>
public sealed class PostCreatedEvent(SysPost newEntity) : AggregateCreatedEvent<SysPost>(newEntity);

/// <summary>岗位修改领域事件：岗位更新成功后发布，Domain 为变更后状态、OldEntity 为变更前快照。</summary>
/// <param name="oldEntity">变更前的岗位快照。</param>
/// <param name="newEntity">变更后的岗位。</param>
public sealed class PostUpdatedEvent(SysPost oldEntity, SysPost newEntity) : AggregateUpdatedEvent<SysPost>(oldEntity, newEntity);

/// <summary>岗位删除领域事件：岗位删除成功后发布，Domain 为删除前的状态。</summary>
/// <param name="newEntity">删除前的岗位。</param>
public sealed class PostDeletedEvent(SysPost newEntity) : AggregateDeletedEvent<SysPost>(newEntity);

/// <summary>岗位恢复领域事件：岗位恢复（解除软删除墓碑）成功后发布，Domain 为恢复后的状态。</summary>
/// <param name="newEntity">恢复后的岗位。</param>
public sealed class PostRestoredEvent(SysPost newEntity) : AggregateRestoredEvent<SysPost>(newEntity);
