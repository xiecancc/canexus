
using Domain.Events;
using Domain.Services.Rules;
using Infrastructure.Shared.Caching;
using MediatR;

namespace Application.Events;
/// <summary>
/// 字典缓存失效监听器（字典领域监听器）：订阅字典数据领域事件并触发字典数据缓存清理，
/// 业务处理器只负责发布事件、不感知清理细节。字典类型本身不参与缓存，仅需响应数据事件。
/// <para>失效口径：失效变更数据所属类型的缓存；类型迁移时旧类型由 OldEntity.DictType 表达，
/// 新旧类型缓存均失效。租户口径与 <see cref="ConfigCacheInvalidationHandler"/> 一致：
/// 按事件实体的 TenantId 追加失效该租户的隔离键，平台键一律失效。</para>
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
                await InvalidateAsync(created.NewEntity.DictType, created.NewEntity.TenantId, cancellationToken);
                break;

            // 类型迁移时旧类型缓存一并失效，保证两个维度都立即读到最新数据
            case DictDataUpdatedEvent updated:
                await InvalidateAsync(updated.NewEntity.DictType, updated.NewEntity.TenantId, cancellationToken);
                if (!string.Equals(updated.OldEntity.DictType, updated.NewEntity.DictType, StringComparison.Ordinal))
                {
                    await InvalidateAsync(updated.OldEntity.DictType, updated.OldEntity.TenantId, cancellationToken);
                }

                break;

            case DictDataDeletedEvent deleted:
                await InvalidateAsync(deleted.NewEntity.DictType, deleted.NewEntity.TenantId, cancellationToken);
                break;

            case DictDataRestoredEvent restored:
                await InvalidateAsync(restored.NewEntity.DictType, restored.NewEntity.TenantId, cancellationToken);
                break;
        }
    }

    /// <summary>失效平台视图键，并在事件属于具体租户时追加失效该租户的隔离键。</summary>
    /// <param name="dictType">字典类型。</param>
    /// <param name="tenantId">事件实体所属租户；null 表示平台级字典数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private async Task InvalidateAsync(string dictType, Guid? tenantId, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(DictRules.DataTypeCacheKey(dictType), cancellationToken);
        if (tenantId is { } id)
        {
            await cache.RemoveAsync(DictRules.DataTypeCacheKey(dictType, id), cancellationToken);
        }
    }
}
