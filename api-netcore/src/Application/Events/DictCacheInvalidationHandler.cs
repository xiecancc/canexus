namespace Application.Events;

using Domain.Events;
using Domain.Services.Rules;
using Infrastructure.Shared.Caching;

/// <summary>
/// 字典缓存失效监听器（字典领域监听器）：订阅字典数据变更事件并触发字典数据缓存清理，
/// 业务处理器只负责发布事件、不感知清理细节。字典类型本身不参与缓存，仅需响应数据变更。
/// <para>失效口径：失效变更数据所属类型的缓存；类型迁移时新旧类型缓存均失效。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="cache">应用缓存端口（承载字典数据缓存清理）。</param>
public sealed class DictCacheInvalidationHandler(ICache cache) : DomainEventHandler<DictDataChangedEvent>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(DictDataChangedEvent domainEvent, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(DictRules.DataTypeCacheKey(domainEvent.DictType), cancellationToken);

        // 类型迁移时旧类型缓存一并失效，保证两个维度都立即读到最新数据
        if (!string.IsNullOrEmpty(domainEvent.OriginalDictType)
            && !string.Equals(domainEvent.OriginalDictType, domainEvent.DictType, StringComparison.Ordinal))
        {
            await cache.RemoveAsync(DictRules.DataTypeCacheKey(domainEvent.OriginalDictType), cancellationToken);
        }
    }
}
