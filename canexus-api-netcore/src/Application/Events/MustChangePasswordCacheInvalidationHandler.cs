
using Domain.Events;
using Infrastructure.Shared.Caching;
using MediatR;

namespace Application.Events;
/// <summary>
/// 强制改密标记缓存失效监听器（用户安全域监听器，P1-R6）：订阅密码重置领域事件，
/// 清除强制改密标记的缓存值，使 MustChangePasswordMiddleware 及时读到最新标记。
/// <para>背景：强制改密标记变更频率极低，但中间件对每个受保护请求都会判断——
/// 直查数据库是纯开销，故标记经双层缓存（Hot 档）读取；缓存失效由本监听器经领域事件驱动，
/// 覆盖两处标记变更点：用户自助改密（置 false）与管理员重置密码（置 true）。</para>
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="cache">应用缓存端口（承载强制改密标记缓存清理）。</param>
public sealed class MustChangePasswordCacheInvalidationHandler(ICache cache) : INotificationHandler<DomainEventNotification>
{
    /// <summary>强制改密标记缓存键前缀（与 MustChangePasswordMiddleware 的读取键一致）。</summary>
    public const string CacheKeyPrefix = "auth:must-change-password:";

    /// <summary>构造强制改密标记缓存键。</summary>
    /// <param name="userId">用户 Id。</param>
    /// <returns>缓存键。</returns>
    public static string CacheKey(Guid userId) => $"{CacheKeyPrefix}{userId}";

    /// <inheritdoc />
    public async Task Handle(DomainEventNotification notification, CancellationToken cancellationToken)
    {
        if (notification.DomainEvent is UserPasswordResetEvent passwordReset)
        {
            await cache.RemoveAsync(CacheKey(passwordReset.UserId), cancellationToken);
        }
    }
}
