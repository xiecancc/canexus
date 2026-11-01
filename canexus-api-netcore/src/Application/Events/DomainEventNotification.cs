
using Domain.Events;
using MediatR;

namespace Application.Events;
/// <summary>
/// 领域事件通知信封：将领域事件包装为 MediatR 进程内通知（INotification），供监听器订阅。
/// <para>非泛型信封使通用监听器（如事件审计日志）只需订阅单一通知类型即可接收全部事件；
/// 领域监听器在处理函数内按事件类型模式匹配、只响应各自关心的事件。
/// 领域层事件模型不依赖 MediatR，替换分发框架仅需调整本层。</para>
/// </summary>
/// <param name="DomainEvent">被发布的领域事件。</param>
public sealed record DomainEventNotification(DomainEvent DomainEvent) : INotification;
