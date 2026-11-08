
using Infrastructure.Caching;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Infrastructure.Tests;

/// <summary>
/// 缓存端口语义回归：验证「只读不回源」不会把未命中固化成假值。
/// <para>背景：HybridCache 的 GetOrCreateAsync 默认会把 factory 结果写入两级缓存；
/// ICache.GetAsync 用 HybridCacheEntryFlags 禁止写入后，「不存在」才真正表示未命中。
/// 会话吊销是单向标记，若 A 实例把 false 缓存进 L1，B 实例登出写入 true 后
/// A 实例仍会放行至 L1 TTL 过期——本用例用两个独立 L1、共享 L2 的 Cache 实例复现该窗口。</para>
/// </summary>
[TestFixture]
public class CacheSemanticsTests
{
    /// <summary>构造共享 L2、各自独立 L1 的缓存实例（模拟两个应用副本）。</summary>
    /// <param name="distributed">共享的分布式缓存（L2）。</param>
    /// <returns>独立的 ICache 实例。</returns>
    private static ICache CreateInstance(IDistributedCache distributed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton(distributed);
        services.AddHybridCache();
        var provider = services.BuildServiceProvider();
        return new Cache(
            provider.GetRequiredService<HybridCache>(),
            provider.GetRequiredService<IOptionsMonitor<CacheOption>>(),
            NullLogger<Cache>.Instance);
    }

    /// <summary>回归：A 实例读到的「未命中」不得进入 L1/L2，B 实例写入 true 后 A 实例立即可见。</summary>
    [Test]
    public async Task GetAsync_UncachedMiss_DoesNotMaskLaterWriteFromAnotherInstance()
    {
        var sharedL2 = new RecordingDistributedCache();
        var instanceA = CreateInstance(sharedL2);
        var instanceB = CreateInstance(sharedL2);
        var key = "auth:session-revoked:" + Guid.NewGuid().ToString("N");

        Assert.That(await instanceA.GetAsync<bool>(key), Is.False, "首次读取应为未命中（未吊销）");

        await instanceB.SetAsync(key, true, CacheTtl.Hot);

        var afterA = await instanceA.GetAsync<bool>(key);
        var afterFresh = await CreateInstance(sharedL2).GetAsync<bool>(key);

        Assert.Multiple(() =>
        {
            Assert.That(afterA, Is.True,
                "另一实例写入吊销标记后，本实例不得再读到缓存中的旧 false（否则登出存在至 TTL 过期的放行窗口）");
            Assert.That(afterFresh, Is.True, "全新实例应经 L2 直接读到吊销标记");
        });
    }

    /// <summary>回归：显式写入的值仍可正常读取，禁止写入 flags 只作用于只读路径。</summary>
    [Test]
    public async Task SetAsync_WritesRemainReadable()
    {
        var cache = CreateInstance(new RecordingDistributedCache());
        var key = "test:explicit-write:" + Guid.NewGuid().ToString("N");

        await cache.SetAsync(key, 42, CacheTtl.Medium);

        Assert.That(await cache.GetAsync<int>(key), Is.EqualTo(42));
    }

    /// <summary>回归：RemoveAsync 后只读路径恢复为未命中。</summary>
    [Test]
    public async Task RemoveAsync_ThenGetAsync_ReturnsDefault()
    {
        var cache = CreateInstance(new RecordingDistributedCache());
        var key = "test:remove:" + Guid.NewGuid().ToString("N");

        await cache.SetAsync(key, true, CacheTtl.Medium);
        await cache.RemoveAsync(key);

        Assert.That(await cache.GetAsync<bool>(key), Is.False);
    }

    /// <summary>
    /// 记录 L2 写入键的分布式缓存装饰器。
    /// <para>必须包装而非直接使用 MemoryDistributedCache：HybridCache 检测到 L2 为内存实现时
    /// 会跳过 L2 写入（L1 已是进程内存），那样无法模拟「多实例共享 Redis L2」的真实行为。</para>
    /// </summary>
    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));

        /// <summary>已写入的 L2 键（用于断言 L2 确实被写入）。</summary>
        public List<string> WrittenKeys { get; } = [];

        /// <inheritdoc />
        public byte[]? Get(string key) => _inner.Get(key);

        /// <inheritdoc />
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(key, token);

        /// <inheritdoc />
        public void Refresh(string key) => _inner.Refresh(key);

        /// <inheritdoc />
        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);

        /// <inheritdoc />
        public void Remove(string key) => _inner.Remove(key);

        /// <inheritdoc />
        public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);

        /// <inheritdoc />
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            WrittenKeys.Add(key);
            _inner.Set(key, value, options);
        }

        /// <inheritdoc />
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            WrittenKeys.Add(key);
            return _inner.SetAsync(key, value, options, token);
        }
    }
}
