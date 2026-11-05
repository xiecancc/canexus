using Domain.Events;
using Infrastructure.Shared.Tenants;
using MediatR;

namespace Application.Events;
/// <summary>
/// 租户访问状态缓存失效监听器：订阅租户创建 / 修改 / 删除 / 恢复领域事件，主动清除
/// <see cref="ITenantAccessValidator"/> 的租户可用性缓存。
/// <para>使租户停用 / 到期 / 删除等状态变更不必等待缓存 TTL 到期即对已登录会话生效；
/// 业务处理器只发布事件，不感知缓存清理细节。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="tenantAccessValidator">租户访问校验器（承载租户状态缓存清理）。</param>
public sealed class TenantAccessCacheInvalidationHandler(ITenantAccessValidator tenantAccessValidator)
    : INotificationHandler<DomainEventNotification>
{
    /// <inheritdoc />
    public async Task Handle(DomainEventNotification notification, CancellationToken cancellationToken)
    {
        var tenantId = notification.DomainEvent switch
        {
            TenantCreatedEvent created => created.NewEntity.Id,
            TenantUpdatedEvent updated => updated.NewEntity.Id,
            TenantDeletedEvent deleted => deleted.NewEntity.Id,
            TenantRestoredEvent restored => restored.NewEntity.Id,
            _ => (Guid?)null
        };

        if (tenantId is not null)
        {
            await tenantAccessValidator.InvalidateAsync(tenantId.Value, cancellationToken);
        }
    }
}
