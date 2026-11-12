
using Domain.Events;
using MediatR;

namespace Application.Events;
/// <summary>
/// 领域事件通知信封：将领域事件包装为 MediatR 进程内通知（INotification），供监听器订阅。
/// <para>非泛型信封使通用监听器（如事件审计日志）只需订阅单一通知类型即可接收全部事件；
/// 领域监听器在处理函数内按事件类型模式匹配、只响应各自关心的事件。
/// 领域层事件模型不依赖 MediatR，替换分发框架仅需调整本层。</para>
/// <para>带副作用的监听器（如站外通知渠道）可用 <paramref name="OutboxMessageId"/> 做幂等去重：
/// Outbox 是至少一次投递，同一消息可能因其他监听器失败而重投；非 Outbox 路径（进程内直发）
/// 该值为 null，监听器应保持原有处理语义。</para>
/// </summary>
/// <param name="DomainEvent">被发布的领域事件。</param>
/// <param name="OutboxMessageId">
/// 来源 Outbox 消息 Id（由 <c>OutboxDeliveryService</c> 发布时填入）；null 表示该通知不经 Outbox 投递。
/// 设为可选参数以兼容全部既有构造点与直接发布场景。
/// </param>
public sealed record DomainEventNotification(DomainEvent DomainEvent, Guid? OutboxMessageId = null) : INotification;
