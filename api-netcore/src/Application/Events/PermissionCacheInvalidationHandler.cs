namespace Application.Events;

using Application.Permissions;
using Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

/// <summary>
/// 权限缓存失效监听器（领域事件订阅方）：订阅用户 / 角色 / 菜单变更事件并触发权限缓存清理，
/// 业务处理器只负责发布事件、不感知清理细节，实现业务逻辑与清理逻辑解耦。
/// <para>失效口径与原内联清理一致：用户事件失效单用户缓存，角色 / 菜单事件全量失效
/// （角色 key / 状态 / 授权菜单、菜单 perms / 挂载点均可能影响任意已授权用户）。</para>
/// <para>扩展约定：新增监听需求（消息推送、通知服务等）只需追加新的 INotificationHandler 实现，
/// 由 MediatR 自动扫描注册，核心业务代码零改动。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="permissionChecker">权限校验器（承载权限缓存清理）。</param>
/// <param name="logger">日志器。</param>
public sealed class PermissionCacheInvalidationHandler(
    IPermissionChecker permissionChecker,
    ILogger<PermissionCacheInvalidationHandler> logger)
    : INotificationHandler<DomainEventNotification<UserChangedEvent>>,
        INotificationHandler<DomainEventNotification<RoleChangedEvent>>,
        INotificationHandler<DomainEventNotification<MenuChangedEvent>>
{
    /// <inheritdoc />
    public async Task Handle(DomainEventNotification<UserChangedEvent> notification, CancellationToken cancellationToken)
    {
        var domainEvent = notification.DomainEvent;
        await permissionChecker.InvalidateAsync(domainEvent.UserId);
        logger.LogInformation("已处理领域事件 {EventName}（{EventType}）：失效用户 {UserId} 的权限缓存",
            domainEvent.EventName, domainEvent.EventType, domainEvent.UserId);
    }

    /// <inheritdoc />
    public async Task Handle(DomainEventNotification<RoleChangedEvent> notification, CancellationToken cancellationToken)
    {
        var domainEvent = notification.DomainEvent;
        await permissionChecker.InvalidateAllAsync();
        logger.LogInformation("已处理领域事件 {EventName}（{EventType}）：全量失效权限缓存",
            domainEvent.EventName, domainEvent.EventType);
    }

    /// <inheritdoc />
    public async Task Handle(DomainEventNotification<MenuChangedEvent> notification, CancellationToken cancellationToken)
    {
        var domainEvent = notification.DomainEvent;
        await permissionChecker.InvalidateAllAsync();
        logger.LogInformation("已处理领域事件 {EventName}（{EventType}）：全量失效权限缓存",
            domainEvent.EventName, domainEvent.EventType);
    }
}
