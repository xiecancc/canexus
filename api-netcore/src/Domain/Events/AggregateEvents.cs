namespace Domain.Events;

using Domain.Entities;

/// <summary>
/// 聚合级事件基类族：按变更类型（创建 / 修改 / 删除 / 恢复）划分的泛型事件基类，
/// 事件类本身即表达变更类型，各基类按语义携带对应聚合状态
/// （<see cref="DomainEvent{TDomain}.Domain"/> 为创建后 / 变更后 / 删除前 / 恢复后状态，
/// 修改类另以 <see cref="AggregateUpdatedEvent{TAggregate}.Before"/> 携带变更前快照）。
/// 状态以聚合实体（浅拷贝快照）承载，监听器经强类型属性访问。
/// <para>不适用场景：无实体状态的事件（如重置密码）继承 <see cref="DomainEvent"/>；
/// 聚合根标识可空的事件（如日志清空）同样直接继承 <see cref="DomainEvent"/>。</para>
/// </summary>
/// <typeparam name="TAggregate">聚合实体类型。</typeparam>
public abstract class AggregateCreatedEvent<TAggregate> : DomainEvent<TAggregate>
    where TAggregate : DomainBase
{
    /// <summary>构造函数：由派生事件传入创建后的聚合（已生成主键）。</summary>
    /// <param name="aggregate">创建后的聚合状态。</param>
    protected AggregateCreatedEvent(TAggregate aggregate)
        : base(aggregate)
    {
    }
}

/// <summary>修改事件基类：携带变更前快照（Before）与变更后状态（继承的 Domain）。</summary>
/// <typeparam name="TAggregate">聚合实体类型。</typeparam>
public abstract class AggregateUpdatedEvent<TAggregate> : DomainEvent<TAggregate>
    where TAggregate : DomainBase
{
    /// <summary>构造函数：由派生事件传入变更前快照与变更后状态。</summary>
    /// <param name="before">变更前状态（发布点经 DomainBase.Snapshot 捕获的浅拷贝快照）。</param>
    /// <param name="after">变更后状态（保存后的聚合，即继承的 Domain）。</param>
    protected AggregateUpdatedEvent(TAggregate before, TAggregate after)
        : base(after)
    {
        Before = before;
    }

    /// <summary>变更前状态（浅拷贝快照）。</summary>
    public TAggregate Before { get; }
}

/// <summary>删除事件基类：携带删除前的聚合状态（Domain，软删除墓碑或物理删除前均可用）。</summary>
/// <typeparam name="TAggregate">聚合实体类型。</typeparam>
public abstract class AggregateDeletedEvent<TAggregate> : DomainEvent<TAggregate>
    where TAggregate : DomainBase
{
    /// <summary>构造函数：由派生事件传入删除前的聚合。</summary>
    /// <param name="aggregate">删除前的聚合状态。</param>
    protected AggregateDeletedEvent(TAggregate aggregate)
        : base(aggregate)
    {
    }
}

/// <summary>恢复事件基类（预留）：携带恢复后的聚合状态（Domain），供软删除恢复（IAggregateRepository.RestoreAsync）业务流程接入。</summary>
/// <typeparam name="TAggregate">聚合实体类型。</typeparam>
public abstract class AggregateRestoredEvent<TAggregate> : DomainEvent<TAggregate>
    where TAggregate : DomainBase
{
    /// <summary>构造函数：由派生事件传入恢复后的聚合。</summary>
    /// <param name="aggregate">恢复后的聚合状态。</param>
    protected AggregateRestoredEvent(TAggregate aggregate)
        : base(aggregate)
    {
    }
}
