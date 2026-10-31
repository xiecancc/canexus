namespace Application.Events;

using Application.Permissions;
using Domain.Events;
using MediatR;

/// <summary>
/// 权限缓存失效监听器（用户 / 角色 / 菜单领域监听器）：订阅权限相关领域事件并触发权限缓存清理，
/// 业务处理器只负责发布事件、不感知清理细节，实现业务逻辑与清理逻辑解耦。
/// <para>失效口径：用户事件失效单用户缓存（创建 / 修改 / 角色重分配 / 重置密码 / 删除），
/// 角色 / 菜单事件全量失效（角色 key / 状态 / 授权菜单、菜单 perms / 挂载点均可能影响任意已授权用户）。</para>
/// <para>扩展约定：新增监听需求（消息推送、通知服务等）只需追加新的 INotificationHandler 实现，
/// 由 MediatR 自动扫描注册，核心业务代码零改动。</para>
/// <para>实现说明：本监听器响应多种事件类型且清理口径不同，受 C# 类单继承限制无法继承多个
/// DomainEventHandler&lt;TEvent&gt; 泛型实例，故直接实现 INotificationHandler 并以模式匹配分发。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="permissionChecker">权限校验器（承载权限缓存清理）。</param>
public sealed class PermissionCacheInvalidationHandler(IPermissionChecker permissionChecker)
    : INotificationHandler<DomainEventNotification>
{
    /// <inheritdoc />
    public async Task Handle(DomainEventNotification notification, CancellationToken cancellationToken)
    {
        switch (notification.DomainEvent)
        {
            // 用户事件：仅失效该用户的权限缓存
            case UserCreatedEvent userCreated:
                await permissionChecker.InvalidateAsync(userCreated.Domain.Id);
                break;
            case UserUpdatedEvent userUpdated:
                await permissionChecker.InvalidateAsync(userUpdated.Domain.Id);
                break;
            case UserRolesUpdatedEvent userRolesUpdated:
                await permissionChecker.InvalidateAsync(userRolesUpdated.Domain.Id);
                break;
            case UserPasswordResetEvent passwordReset:
                await permissionChecker.InvalidateAsync(passwordReset.UserId);
                break;
            case UserDeletedEvent userDeleted:
                await permissionChecker.InvalidateAsync(userDeleted.Domain.Id);
                break;

            // 角色 / 菜单事件：影响任意持有（已授权）用户的权限，全量失效
            case RoleCreatedEvent or RoleUpdatedEvent or RoleDeletedEvent
                or MenuCreatedEvent or MenuUpdatedEvent or MenuDeletedEvent:
                await permissionChecker.InvalidateAllAsync();
                break;
        }
    }
}
