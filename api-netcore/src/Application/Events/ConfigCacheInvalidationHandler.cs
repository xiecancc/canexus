namespace Application.Events;

using Domain.Events;
using Domain.Services.Rules;
using Infrastructure.Shared.Caching;

/// <summary>
/// 参数配置缓存失效监听器（配置领域监听器）：订阅参数配置变更事件并触发配置值缓存清理，
/// 业务处理器只负责发布事件、不感知清理细节。
/// <para>失效口径：失效变更配置键的缓存；键名变更时新旧键缓存均失效。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="cache">应用缓存端口（承载配置值缓存清理）。</param>
public sealed class ConfigCacheInvalidationHandler(ICache cache) : DomainEventHandler<ConfigChangedEvent>
{
    /// <inheritdoc />
    protected override async Task HandleAsync(ConfigChangedEvent domainEvent, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(ConfigRules.ValueCacheKey(domainEvent.ConfigKey), cancellationToken);

        // 键名变更时旧键缓存一并失效，保证两个维度都立即读到最新值
        if (!string.IsNullOrEmpty(domainEvent.OriginalConfigKey)
            && !string.Equals(domainEvent.OriginalConfigKey, domainEvent.ConfigKey, StringComparison.Ordinal))
        {
            await cache.RemoveAsync(ConfigRules.ValueCacheKey(domainEvent.OriginalConfigKey), cancellationToken);
        }
    }
}
