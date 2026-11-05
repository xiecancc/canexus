
using Domain.Entities;
using Domain.Events;
using Domain.Repositories;
using Domain.Services.Auth;
using Domain.Shared.Enums;
using MediatR;

namespace Application.Events;
/// <summary>
/// 令牌版本失效监听器（P1-A）：订阅用户 / 角色安全相关领域事件，递增受影响用户的令牌版本，
/// 使已签发的 accessToken 在下一次请求即被拒绝（请求期由 TokenValidationGuard 比对版本）。
/// <para>失效口径：改密 / 删除 / 停用 / 角色重分配 → 单用户全量失效；
/// 角色状态或 roleKey 变更 → 该角色全部成员失效（角色权限集合变更由权限缓存失效监听器负责，无需令牌失效）。</para>
/// <para>幂等性：事件可能因 Outbox 重试重复投递，重复递增版本只会额外触发一次会话失效，不影响正确性。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="tokenVersionService">令牌版本服务。</param>
/// <param name="userRoleRepository">用户-角色关联仓储（角色级变更时解析受影响成员）。</param>
public sealed class TokenVersionInvalidationHandler(
    ITokenVersionService tokenVersionService,
    IDomainRepository<SysUserRole> userRoleRepository)
    : INotificationHandler<DomainEventNotification>
{
    /// <inheritdoc />
    public async Task Handle(DomainEventNotification notification, CancellationToken cancellationToken)
    {
        switch (notification.DomainEvent)
        {
            // 改密（自助改密 / 管理员重置）与删除：该用户全部会话立即失效
            case UserPasswordResetEvent passwordReset:
                await tokenVersionService.RevokeAllAsync(passwordReset.UserId, cancellationToken);
                break;

            case UserDeletedEvent userDeleted:
                await tokenVersionService.RevokeAllAsync(userDeleted.NewEntity.Id, cancellationToken);
                break;

            // 状态变更为停用：旧 accessToken 立即失效（启用方向无需强制下线）
            case UserUpdatedEvent userUpdated when userUpdated.NewEntity.Status != CommonStatus.Enabled
                && userUpdated.OldEntity.Status != userUpdated.NewEntity.Status:
                await tokenVersionService.RevokeAllAsync(userUpdated.NewEntity.Id, cancellationToken);
                break;

            // 角色重分配：旧令牌中的 roleId / role 声明已过期，全量失效后由用户重新登录获取新角色
            case UserRolesUpdatedEvent userRolesUpdated:
                await tokenVersionService.RevokeAllAsync(userRolesUpdated.NewEntity.Id, cancellationToken);
                break;

            // 角色状态 / roleKey 变更：影响该角色全部成员的令牌声明，批量失效（权限集合变更走权限缓存失效）
            case RoleUpdatedEvent roleUpdated when roleUpdated.OldEntity.Status != roleUpdated.NewEntity.Status
                || !string.Equals(roleUpdated.OldEntity.RoleKey, roleUpdated.NewEntity.RoleKey, StringComparison.Ordinal):
                var members = await userRoleRepository.GetListAsync(
                    [ur => ur.RoleId == roleUpdated.NewEntity.Id],
                    cancellationToken: cancellationToken);
                await tokenVersionService.RevokeAllForUsersAsync(
                    members.Select(member => member.UserId).ToList(),
                    cancellationToken);
                break;
        }
    }
}
