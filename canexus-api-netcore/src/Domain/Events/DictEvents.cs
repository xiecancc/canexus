
using Domain.Entities;

namespace Domain.Events;
/// <summary>字典类型创建领域事件：字典类型创建成功后发布，Domain 为创建后的状态。</summary>
/// <param name="newEntity">创建后的字典类型。</param>
public sealed class DictTypeCreatedEvent(SysDictType newEntity) : AggregateCreatedEvent<SysDictType>(newEntity);

/// <summary>字典类型修改领域事件：字典类型更新成功后发布，Domain 为变更后状态、OldEntity 为变更前快照（类型键名不可变更，无迁移语义）。</summary>
/// <param name="oldEntity">变更前的字典类型快照。</param>
/// <param name="newEntity">变更后的字典类型。</param>
public sealed class DictTypeUpdatedEvent(SysDictType oldEntity, SysDictType newEntity) : AggregateUpdatedEvent<SysDictType>(oldEntity, newEntity);

/// <summary>字典类型删除领域事件：字典类型删除成功后发布，Domain 为删除前的状态。</summary>
/// <param name="newEntity">删除前的字典类型。</param>
public sealed class DictTypeDeletedEvent(SysDictType newEntity) : AggregateDeletedEvent<SysDictType>(newEntity);

/// <summary>字典类型恢复领域事件：字典类型恢复（解除软删除墓碑）成功后发布，Domain 为恢复后的状态。</summary>
/// <param name="newEntity">恢复后的字典类型。</param>
public sealed class DictTypeRestoredEvent(SysDictType newEntity) : AggregateRestoredEvent<SysDictType>(newEntity);

/// <summary>字典数据创建领域事件：字典数据创建成功后发布，Domain 为创建后的状态。</summary>
/// <param name="newEntity">创建后的字典数据。</param>
public sealed class DictDataCreatedEvent(SysDictData newEntity) : AggregateCreatedEvent<SysDictData>(newEntity);

/// <summary>字典数据修改领域事件：字典数据更新成功后发布，Domain 为变更后状态、OldEntity 为变更前快照——类型迁移时旧类型由 OldEntity.DictType 表达，监听器可据此双失效缓存。</summary>
/// <param name="oldEntity">变更前的字典数据快照。</param>
/// <param name="newEntity">变更后的字典数据。</param>
public sealed class DictDataUpdatedEvent(SysDictData oldEntity, SysDictData newEntity) : AggregateUpdatedEvent<SysDictData>(oldEntity, newEntity);

/// <summary>字典数据删除领域事件：字典数据删除成功后发布，Domain 为删除前的状态。</summary>
/// <param name="newEntity">删除前的字典数据。</param>
public sealed class DictDataDeletedEvent(SysDictData newEntity) : AggregateDeletedEvent<SysDictData>(newEntity);

/// <summary>字典数据恢复领域事件：字典数据恢复（解除软删除墓碑）成功后发布，Domain 为恢复后的状态。</summary>
/// <param name="newEntity">恢复后的字典数据。</param>
public sealed class DictDataRestoredEvent(SysDictData newEntity) : AggregateRestoredEvent<SysDictData>(newEntity);
