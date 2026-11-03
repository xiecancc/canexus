using Domain.Entities;

namespace Domain.Events;

/// <summary>
/// 领域事件根基类（非泛型）：仅承载全部事件共有的最小元数据。
/// <para>事件类型由事件类本身表达——<see cref="EventName"/>（短类名）即事件类型的唯一标识，
/// 不再维护独立的业务类型字符串（进程内路由靠 CLR 类型模式匹配，字符串仅用于日志展示；
/// 若未来事件跨进程经消息队列分发，再引入显式命名契约）。事件不可变——仅在业务变更成功后
/// 由应用层经 <c>IDomainEventPublisher</c> 发布，监听器订阅并各自决定响应动作，
/// 实现业务逻辑与横切逻辑解耦。</para>
/// </summary>
public abstract class DomainEvent
{
    /// <summary>事件名称：事件类型的短类名（如 UserUpdatedEvent），供监听器路由与日志排查。</summary>
    public string EventName => GetType().Name;

    /// <summary>事件发生时间（服务器本地时间，与审计字段 CreatedAt 口径一致）。</summary>
    public DateTime OccurredAt { get; } = DateTime.Now;
}

/// <summary>
/// 领域事件泛型基类：携带事件关联的领域实体（聚合根或关联实体），
/// 监听器经 <see cref="Domain"/> 直接读取实体状态，无需裸 Id 或弱类型载荷。
/// </summary>
/// <typeparam name="TDomain">领域实体类型。</typeparam>
/// <remarks>构造函数：由派生事件传入关联的领域实体 <paramref name="entity"/>。</remarks>
/// <param name="entity">事件关联的领域实体。</param>
public abstract class DomainEvent<TDomain>(TDomain entity) : DomainEvent
    where TDomain : DomainBase
{
    /// <summary>事件关联的领域实体（按事件语义为创建后 / 变更后 / 删除前等状态，见具体基类说明）。</summary>
    public TDomain NewEntity { get; } = entity;
}
