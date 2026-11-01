
using Domain.Entities;

namespace Domain.Events;
/// <summary>
/// 聚合级事件基类族：按变更类型（创建 / 修改 / 删除 / 恢复）划分的泛型事件基类，
/// 事件类本身即表达变更类型，各基类按语义携带对应聚合状态
/// （<see cref="DomainEvent{TDomain}.Domain"/> 为创建后 / 变更后 / 删除前 / 恢复后状态，
/// 修改类另以 <see cref="AggregateUpdatedEvent{TAggregate}.OldEntity"/> 携带变更前快照）。
/// 状态以聚合实体（浅拷贝快照）承载，监听器经强类型属性访问。
/// <para>不适用场景：无实体状态的事件（如重置密码）继承 <see cref="DomainEvent"/>；
/// 聚合根标识可空的事件（如日志清空）同样直接继承 <see cref="DomainEvent"/>。</para>
/// </summary>
/// <typeparam name="TAggregate">聚合实体类型。</typeparam>
/// <remarks>构造函数：由派生事件传入创建后的聚合（已生成主键）。</remarks>
/// <param name="entity">创建后的聚合状态。</param>
public abstract class AggregateCreatedEvent<TAggregate>(TAggregate entity) : DomainEvent<TAggregate>(entity)
    where TAggregate : DomainBase
{
}

/// <summary>修改事件基类：携带变更前快照（OldEntity）与变更后状态（继承的 Domain）。</summary>
/// <typeparam name="TAggregate">聚合实体类型。</typeparam>
/// <remarks>构造函数：由派生事件传入变更前快照与变更后状态。</remarks>
/// <param name="oldEntity">变更前状态（发布点经 DomainBase.Snapshot 捕获的浅拷贝快照）。</param>
/// <param name="entity">变更后状态（保存后的聚合，即继承的 Domain）。</param>
public abstract class AggregateUpdatedEvent<TAggregate>(TAggregate oldEntity, TAggregate entity) : DomainEvent<TAggregate>(entity)
    where TAggregate : DomainBase
{

    /// <summary>变更前状态（浅拷贝快照）。</summary>
    public TAggregate OldEntity { get; } = oldEntity;
}

/// <summary>删除事件基类：携带删除前的聚合状态（Domain，软删除墓碑或物理删除前均可用）。</summary>
/// <typeparam name="TAggregate">聚合实体类型。</typeparam>
/// <remarks>构造函数：由派生事件传入删除前的聚合。</remarks>
/// <param name="entity">删除前的聚合状态。</param>
public abstract class AggregateDeletedEvent<TAggregate>(TAggregate entity) : DomainEvent<TAggregate>(entity)
    where TAggregate : DomainBase
{
}

/// <summary>恢复事件基类：携带恢复后的聚合状态（Domain），由聚合恢复命令处理器（AggregateRestoreCommandHandler）在实际解除软删除墓碑后发布。</summary>
/// <typeparam name="TAggregate">聚合实体类型。</typeparam>
/// <remarks>构造函数：由派生事件传入恢复后的聚合。</remarks>
/// <param name="entity">恢复后的聚合状态。</param>
public abstract class AggregateRestoredEvent<TAggregate>(TAggregate entity) : DomainEvent<TAggregate>(entity)
    where TAggregate : DomainBase
{
}
