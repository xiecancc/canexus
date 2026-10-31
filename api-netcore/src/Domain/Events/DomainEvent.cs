namespace Domain.Events;

/// <summary>
/// 领域事件基类：核心业务流程关键节点的通用事件模型（最小契约）。
/// <para>事件不可变——仅在业务变更成功后由应用层经 <c>IDomainEventPublisher</c> 发布，
/// 监听器按事件类型订阅并各自决定响应动作（缓存清理、消息推送、通知服务等），
/// 实现业务逻辑与横切逻辑解耦。</para>
/// </summary>
public abstract class DomainEvent
{
    /// <summary>空载荷（只读共享实例，事件不可变，无并发写风险）。</summary>
    private static readonly IReadOnlyDictionary<string, object?> EmptyPayload = new Dictionary<string, object?>();

    /// <summary>事件名称：事件类型的短类名（如 UserChangedEvent），供监听器路由与日志排查。</summary>
    public string EventName => GetType().Name;

    /// <summary>事件类型：业务级命名（{模块}.{变更}，如 user.changed），与类名解耦，便于跨端消费与检索。</summary>
    public string EventType { get; }

    /// <summary>事件发生时间（服务器本地时间，与审计字段 CreatedAt 口径一致）。</summary>
    public DateTime OccurredAt { get; } = DateTime.Now;

    /// <summary>聚合根标识：事件所属聚合根主键；非聚合级事件为 null。</summary>
    public Guid? AggregateId { get; }

    /// <summary>事件载荷：携带监听器所需的最小业务数据（键为 camelCase 命名）。</summary>
    public IReadOnlyDictionary<string, object?> Payload { get; }

    /// <summary>构造函数：由子类传入事件类型、聚合根标识与载荷。</summary>
    /// <param name="eventType">事件类型（业务级命名，如 user.changed）。</param>
    /// <param name="aggregateId">聚合根标识；非聚合级事件传 null。</param>
    /// <param name="payload">事件载荷；缺省为空载荷。</param>
    protected DomainEvent(string eventType, Guid? aggregateId = null, IReadOnlyDictionary<string, object?>? payload = null)
    {
        EventType = eventType;
        AggregateId = aggregateId;
        Payload = payload ?? EmptyPayload;
    }
}
