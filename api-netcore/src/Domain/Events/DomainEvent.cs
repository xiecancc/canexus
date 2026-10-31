namespace Domain.Events;

/// <summary>
/// 领域事件基类：核心业务流程关键节点的通用事件模型（最小契约）。
/// <para>事件不可变——仅在业务变更成功后由应用层经 <c>IDomainEventPublisher</c> 发布，
/// 监听器按事件类型订阅并各自决定响应动作（缓存清理、消息推送、通知服务等），
/// 实现业务逻辑与横切逻辑解耦。</para>
/// <para>属性设计原则：只保留全部事件共有的元数据；业务数据一律以派生事件的强类型属性承载
/// （如修改类事件的 Before / After 状态），不再提供弱类型载荷字典，避免与类型化属性重复。</para>
/// </summary>
public abstract class DomainEvent
{
    /// <summary>事件名称：事件类型的短类名（如 UserUpdatedEvent），供监听器路由与日志排查。</summary>
    public string EventName => GetType().Name;

    /// <summary>事件类型：业务级命名（{模块}.{变更}，如 user.updated），与类名解耦，便于跨端消费与检索。</summary>
    public string EventType { get; }

    /// <summary>事件发生时间（服务器本地时间，与审计字段 CreatedAt 口径一致）。</summary>
    public DateTime OccurredAt { get; } = DateTime.Now;

    /// <summary>聚合根标识：事件所属聚合根主键；非聚合级事件为 null。</summary>
    public Guid? AggregateId { get; }

    /// <summary>构造函数：由派生事件传入事件类型与聚合根标识。</summary>
    /// <param name="eventType">事件类型（业务级命名，如 user.updated）。</param>
    /// <param name="aggregateId">聚合根标识；非聚合级事件传 null。</param>
    protected DomainEvent(string eventType, Guid? aggregateId = null)
    {
        EventType = eventType;
        AggregateId = aggregateId;
    }
}
