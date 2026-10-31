namespace Domain.Events;

using Domain.Entities;

/// <summary>字典类型创建领域事件：字典类型创建成功后发布，Domain 为创建后的状态。</summary>
/// <param name="Entity">创建后的字典类型。</param>
public sealed class DictTypeCreatedEvent(SysDictType entity) : AggregateCreatedEvent<SysDictType>(entity);

/// <summary>字典类型修改领域事件：字典类型更新成功后发布，Domain 为变更后状态、OldEntity 为变更前快照（类型键名不可变更，无迁移语义）。</summary>
/// <param name="Before">变更前的字典类型快照。</param>
/// <param name="After">变更后的字典类型。</param>
public sealed class DictTypeUpdatedEvent(SysDictType before, SysDictType after) : AggregateUpdatedEvent<SysDictType>(before, after);

/// <summary>字典类型删除领域事件：字典类型删除成功后发布，Domain 为删除前的状态。</summary>
/// <param name="Entity">删除前的字典类型。</param>
public sealed class DictTypeDeletedEvent(SysDictType entity) : AggregateDeletedEvent<SysDictType>(entity);

/// <summary>字典数据创建领域事件：字典数据创建成功后发布，Domain 为创建后的状态。</summary>
/// <param name="Entity">创建后的字典数据。</param>
public sealed class DictDataCreatedEvent(SysDictData entity) : AggregateCreatedEvent<SysDictData>(entity);

/// <summary>字典数据修改领域事件：字典数据更新成功后发布，Domain 为变更后状态、OldEntity 为变更前快照——类型迁移时旧类型由 OldEntity.DictType 表达，监听器可据此双失效缓存。</summary>
/// <param name="Before">变更前的字典数据快照。</param>
/// <param name="After">变更后的字典数据。</param>
public sealed class DictDataUpdatedEvent(SysDictData before, SysDictData after) : AggregateUpdatedEvent<SysDictData>(before, after);

/// <summary>字典数据删除领域事件：字典数据删除成功后发布，Domain 为删除前的状态。</summary>
/// <param name="Entity">删除前的字典数据。</param>
public sealed class DictDataDeletedEvent(SysDictData entity) : AggregateDeletedEvent<SysDictData>(entity);
