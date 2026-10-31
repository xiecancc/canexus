namespace Application.Events;

using Domain.Events;
using Domain.Services.Rules;
using Infrastructure.Shared.Caching;
using MediatR;

/// <summary>
/// 字典缓存失效监听器（字典领域监听器）：订阅字典数据领域事件并触发字典数据缓存清理，
/// 业务处理器只负责发布事件、不感知清理细节。字典类型本身不参与缓存，仅需响应数据事件。
/// <para>失效口径：失效变更数据所属类型的缓存；类型迁移时旧类型由 Before.DictType 表达，
/// 新旧类型缓存均失效。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="cache">应用缓存端口（承载字典数据缓存清理）。</param>
public sealed class DictCacheInvalidationHandler(ICache cache) : INotificationHandler<DomainEventNotification>
{
    /// <inheritdoc />
    public async Task Handle(DomainEventNotification notification, CancellationToken cancellationToken)
    {
        switch (notification.DomainEvent)
        {
            case DictDataCreatedEvent created:
                await cache.RemoveAsync(DictRules.DataTypeCacheKey(created.Entity.DictType), cancellationToken);
                break;

            // 类型迁移时旧类型缓存一并失效，保证两个维度都立即读到最新数据
            case DictDataUpdatedEvent updated:
                await cache.RemoveAsync(DictRules.DataTypeCacheKey(updated.After.DictType), cancellationToken);
                if (!string.Equals(updated.Before.DictType, updated.After.DictType, StringComparison.Ordinal))
                {
                    await cache.RemoveAsync(DictRules.DataTypeCacheKey(updated.Before.DictType), cancellationToken);
                }

                break;

            case DictDataDeletedEvent deleted:
                await cache.RemoveAsync(DictRules.DataTypeCacheKey(deleted.Entity.DictType), cancellationToken);
                break;
        }
    }
}
