namespace Domain.Events;

using Domain.Entities;

/// <summary>
/// 创建事件基类：携带创建后的聚合状态。
/// <para>本基类族（Created / Updated / Deleted / Restored）按变更类型划分聚合级事件基类，
/// 事件类本身即表达变更类型，各基类按语义携带对应聚合状态（创建后 / 变更前后 / 删除前 / 恢复后）；
/// 状态以聚合根实体（浅拷贝快照）承载，监听器经强类型属性访问，无需弱类型载荷。
/// 聚合根标识可空的事件（如日志清空）不适用本基类族，直接继承 <see cref="DomainEvent"/>。</para>
/// </summary>
/// <typeparam name="TEntity">聚合根实体类型。</typeparam>
public abstract class CreatedEvent<TEntity> : DomainEvent
    where TEntity : AggregateBase
{
    /// <summary>构造函数：由派生事件传入事件类型与创建后的聚合。</summary>
    /// <param name="eventType">事件类型（业务级命名，如 user.created）。</param>
    /// <param name="entity">创建后的聚合（已生成主键）。</param>
    protected CreatedEvent(string eventType, TEntity entity)
        : base(eventType, entity.Id)
    {
        Entity = entity;
    }

    /// <summary>创建后的聚合状态。</summary>
    public TEntity Entity { get; }
}

/// <summary>修改事件基类：携带变更前 / 变更后的聚合状态（Before 为变更前浅拷贝快照，After 为保存后的聚合）。</summary>
/// <typeparam name="TEntity">聚合根实体类型。</typeparam>
public abstract class UpdatedEvent<TEntity> : DomainEvent
    where TEntity : AggregateBase
{
    /// <summary>构造函数：由派生事件传入事件类型与变更前后状态。</summary>
    /// <param name="eventType">事件类型（业务级命名，如 user.updated）。</param>
    /// <param name="before">变更前状态（发布点捕获的浅拷贝快照）。</param>
    /// <param name="after">变更后状态（保存后的聚合）。</param>
    protected UpdatedEvent(string eventType, TEntity before, TEntity after)
        : base(eventType, after.Id)
    {
        Before = before;
        After = after;
    }

    /// <summary>变更前状态（浅拷贝快照）。</summary>
    public TEntity Before { get; }

    /// <summary>变更后状态。</summary>
    public TEntity After { get; }
}

/// <summary>删除事件基类：携带删除前的聚合状态（软删除墓碑或物理删除前均可用）。</summary>
/// <typeparam name="TEntity">聚合根实体类型。</typeparam>
public abstract class DeletedEvent<TEntity> : DomainEvent
    where TEntity : AggregateBase
{
    /// <summary>构造函数：由派生事件传入事件类型与删除前的聚合。</summary>
    /// <param name="eventType">事件类型（业务级命名，如 user.deleted）。</param>
    /// <param name="entity">删除前的聚合。</param>
    protected DeletedEvent(string eventType, TEntity entity)
        : base(eventType, entity.Id)
    {
        Entity = entity;
    }

    /// <summary>删除前的聚合状态。</summary>
    public TEntity Entity { get; }
}

/// <summary>恢复事件基类（预留）：携带恢复后的聚合状态，供软删除恢复（IAggregateRepository.RestoreAsync）业务流程接入。</summary>
/// <typeparam name="TEntity">聚合根实体类型。</typeparam>
public abstract class RestoredEvent<TEntity> : DomainEvent
    where TEntity : AggregateBase
{
    /// <summary>构造函数：由派生事件传入事件类型与恢复后的聚合。</summary>
    /// <param name="eventType">事件类型（业务级命名，如 user.restored）。</param>
    /// <param name="entity">恢复后的聚合。</param>
    protected RestoredEvent(string eventType, TEntity entity)
        : base(eventType, entity.Id)
    {
        Entity = entity;
    }

    /// <summary>恢复后的聚合状态。</summary>
    public TEntity Entity { get; }
}
