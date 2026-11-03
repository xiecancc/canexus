
namespace Infrastructure.Shared.Locking;
/// <summary>
/// 分布式锁端口：保护跨实例的读-改-写关键区（登录失败计数、权限版本号递增、种子初始化幂等等）。
/// <para>获取失败（等待超时 / 未启用分布式后端）返回 <c>null</c>，由调用方决定降级策略——
/// 本框架内锁均为「正确性增强」而非「正确性依赖」，关键路径均有兜底（限流、TTL、幂等判重），
/// 拿不到锁时按无锁路径继续，避免锁组件故障阻断业务。</para>
/// </summary>
public interface IDistributedLock
{
    /// <summary>尝试获取分布式锁。</summary>
    /// <param name="key">锁键（业务语义，内部自动附加实例前缀与 lock: 段）。</param>
    /// <param name="expiry">锁过期时间（持有者崩溃后的自动释放保障，应略大于关键区最长耗时）。</param>
    /// <param name="waitTimeout">获取等待超时（null 表示单次尝试不等待）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>锁句柄（DisposeAsync 释放）；未获取到返回 null。</returns>
    public ValueTask<IAsyncDisposable?> AcquireAsync(
        string key,
        TimeSpan expiry,
        TimeSpan? waitTimeout = null,
        CancellationToken cancellationToken = default);
}
