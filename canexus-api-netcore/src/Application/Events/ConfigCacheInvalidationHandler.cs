
using Domain.Events;
using Domain.Services.Rules;
using Infrastructure.Shared.Caching;
using MediatR;

namespace Application.Events;
/// <summary>
/// 参数配置缓存失效监听器（配置领域监听器）：订阅参数配置领域事件并触发配置值缓存清理，
/// 业务处理器只负责发布事件、不感知清理细节。
/// <para>失效口径：失效变更配置键的缓存；键名变更时旧键名由 OldEntity.ConfigKey 表达，新旧键缓存均失效。
/// 租户口径：租户级配置同时失效该租户的隔离键，平台视图（可见全部数据）的平台键一律失效；
/// 依据事件载荷里的 TenantId 而非请求上下文，Outbox 后台重投时同样覆盖正确租户。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="cache">应用缓存端口（承载配置值缓存清理）。</param>
public sealed class ConfigCacheInvalidationHandler(ICache cache) : INotificationHandler<DomainEventNotification>
{
    /// <inheritdoc />
    public async Task Handle(DomainEventNotification notification, CancellationToken cancellationToken)
    {
        switch (notification.DomainEvent)
        {
            case ConfigCreatedEvent created:
                await InvalidateAsync(created.NewEntity.ConfigKey, created.NewEntity.TenantId, cancellationToken);
                break;

            // 键名变更时旧键缓存一并失效，保证两个维度都立即读到最新值
            case ConfigUpdatedEvent updated:
                await InvalidateAsync(updated.NewEntity.ConfigKey, updated.NewEntity.TenantId, cancellationToken);
                if (!string.Equals(updated.OldEntity.ConfigKey, updated.NewEntity.ConfigKey, StringComparison.Ordinal))
                {
                    await InvalidateAsync(updated.OldEntity.ConfigKey, updated.OldEntity.TenantId, cancellationToken);
                }

                break;

            case ConfigDeletedEvent deleted:
                await InvalidateAsync(deleted.NewEntity.ConfigKey, deleted.NewEntity.TenantId, cancellationToken);
                break;

            case ConfigRestoredEvent restored:
                await InvalidateAsync(restored.NewEntity.ConfigKey, restored.NewEntity.TenantId, cancellationToken);
                break;
        }
    }

    /// <summary>失效平台视图键，并在事件属于具体租户时追加失效该租户的隔离键。</summary>
    /// <param name="configKey">参数键名。</param>
    /// <param name="tenantId">事件实体所属租户；null 表示平台级配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task InvalidateAsync(string configKey, Guid? tenantId, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(ConfigRules.ValueCacheKey(configKey), cancellationToken);
        if (tenantId is { } id)
        {
            await cache.RemoveAsync(ConfigRules.ValueCacheKey(configKey, id), cancellationToken);
        }
    }
}
