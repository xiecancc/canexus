namespace Infrastructure.Shared.Caching;

/// <summary>
/// 应用缓存端口：封装"进程内存（L1）+ Redis（L2）"双层缓存的统一读写与失效语义。
/// <para>实现位于 Infrastructure（当前基于 HybridCache）；应用层只依赖本端口，
/// 不感知任何缓存技术细节。各档位默认策略：L1 本地短过期、L2 分布式长过期，
/// 在性能（L1 命中零网络开销）与一致性（L2 统一视图、到期自动回源）之间取得平衡。</para>
/// </summary>
public interface ICache
{
    /// <summary>读取缓存；未命中时执行工厂方法取值并按档位写入 L1 / L2（防击穿：同 key 并发只执行一次工厂）。</summary>
    /// <typeparam name="T">缓存值类型（需可 JSON 序列化且非 null）。</typeparam>
    /// <param name="key">缓存键（业务语义，建议带模块前缀，如 config:key:xxx）。</param>
    /// <param name="factory">回源工厂（必须返回非 null 值）。</param>
    /// <param name="ttl">TTL 档位（决定 L1 / L2 各自的过期时间）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>缓存值或回源结果。</returns>
    ValueTask<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CacheTtl ttl = CacheTtl.Medium, CancellationToken cancellationToken = default) where T : notnull;

    /// <summary>读取缓存；未命中返回 default（值类型为 0 / 引用类型为 null），不回源。</summary>
    /// <typeparam name="T">缓存值类型。</typeparam>
    /// <param name="key">缓存键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>缓存值；未命中返回 default。</returns>
    ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>写入缓存：同时写入 L1（短过期）与 L2（长过期）。</summary>
    /// <typeparam name="T">缓存值类型。</typeparam>
    /// <param name="key">缓存键。</param>
    /// <param name="value">缓存值。</param>
    /// <param name="ttl">TTL 档位。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task SetAsync<T>(string key, T value, CacheTtl ttl = CacheTtl.Medium, CancellationToken cancellationToken = default);

    /// <summary>写入缓存（显式两级过期时间：需要精确控制时使用，如登录锁定 10 分钟）。</summary>
    /// <typeparam name="T">缓存值类型。</typeparam>
    /// <param name="key">缓存键。</param>
    /// <param name="value">缓存值。</param>
    /// <param name="profile">L1 / L2 各自的过期时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task SetAsync<T>(string key, T value, CacheTtlProfile profile, CancellationToken cancellationToken = default);

    /// <summary>按缓存键失效：同时清除本实例 L1 与 L2 分布式条目。</summary>
    /// <param name="key">缓存键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
