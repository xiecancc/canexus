using System.Linq.Expressions;
using Application.Permissions;
using Domain.Entities;
using Domain.Repositories;
using Domain.Shared.Paging;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Locking;
using Microsoft.Extensions.Logging;

namespace Application.Tests.Permissions;

/// <summary>
/// 权限全局版本号（P1-6）回归：版本号 key 丢失后绝不回退 0，而是重建安全单调版本号并以 Never 档写回，
/// 从根本上消除历史 v0 用户权限缓存 key 复活导致的越权窗口；全量失效的递增语义与既有缓存命中行为保持不变。
/// </summary>
[TestFixture]
public class PermissionCheckerTests
{
    /// <summary>全局版本号缓存 key（与 PermissionChecker 内部常量一致）。</summary>
    private const string VersionCacheKey = PermissionChecker.CacheKeyPrefix + "version";

    /// <summary>构造用户权限缓存 key（与 PermissionChecker 的 key 规则一致）。</summary>
    /// <param name="version">全局版本号。</param>
    /// <param name="userId">用户 Id。</param>
    /// <returns>用户权限缓存 key。</returns>
    private static string UserKey(long version, Guid userId)
        => $"{PermissionChecker.CacheKeyPrefix}{version}:{userId}";

    /// <summary>缓存 miss 时不返回 0，而是重建安全版本号并以 Never 档写回、输出告警，且失效不再使用 v0 key。</summary>
    [Test]
    public async Task CacheMiss_RebuildsSafeVersion_InsteadOfZero()
    {
        var userId = Guid.NewGuid();
        var (checker, cache, logger, _) = CreateFixture();

        await checker.InvalidateAsync(userId);

        var version = cache.ReadVersion();
        Assert.Multiple(() =>
        {
            Assert.That(version, Is.GreaterThan(1_000_000_000L), "安全版本号应为远大于历史小整数版本号的大值，绝不回退 0");
            Assert.That(cache.Writes.Any(write => write.Key == VersionCacheKey && write.Ttl == CacheTtl.Never), Is.True,
                "重建的版本号必须以 Never 档写回缓存");
            Assert.That(cache.RemovedKeys.Contains(UserKey(0, userId)), Is.False, "失效不得使用 v0 key");
            Assert.That(cache.RemovedKeys.Contains(UserKey(version, userId)), Is.True, "失效应使用重建后的版本号 key");
            Assert.That(logger.Entries.Any(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("版本号")), Is.True,
                "版本号重建应输出 Warning 告警");
        });
    }

    /// <summary>模拟 Redis 重启 / flush / 淘汰导致版本号 key 丢失后，滞留的 v0 历史用户权限 key 不得被命中。</summary>
    [Test]
    public async Task VersionKeyLost_LegacyV0PermissionKeyIsNotHit()
    {
        var userId = Guid.NewGuid();
        var (checker, cache, logger, _) = CreateFixture();
        HashSet<string> stalePermissions = ["stale:perm"];
        cache.Seed(UserKey(0, userId), stalePermissions);
        cache.Seed(VersionCacheKey, 5L);

        var beforeLoss = await checker.GetPermissionsAsync(userId);

        // 版本号 key 丢失，v0 历史用户权限 key 仍滞留
        cache.Drop(VersionCacheKey);
        cache.ClearReadKeys();

        var afterLoss = await checker.GetPermissionsAsync(userId);
        var version = cache.ReadVersion();

        Assert.Multiple(() =>
        {
            Assert.That(beforeLoss.Contains("stale:perm"), Is.False, "版本号存在时不得命中 v0 历史 key");
            Assert.That(afterLoss.Contains("stale:perm"), Is.False, "版本号丢失后旧 v0 用户权限缓存 key 不得被命中（越权窗口已消除）");
            Assert.That(cache.ReadKeys.Contains(UserKey(0, userId)), Is.False, "重建后不得再读取 v0 key");
            Assert.That(version, Is.GreaterThan(5L), "应重建出大于丢失前版本号的安全版本号");
            Assert.That(cache.Writes.Any(write => write.Key == VersionCacheKey && write.Ttl == CacheTtl.Never), Is.True,
                "重建版本须以 Never 档写回");
            Assert.That(logger.Entries.Any(entry => entry.Level == LogLevel.Warning), Is.True, "版本号丢失应告警");
        });
    }

    /// <summary>版本号存在时 InvalidateAllAsync 仍在现有值上 +1，且经分布式锁保护、不触发重建告警。</summary>
    [Test]
    public async Task InvalidateAllAsync_IncrementsExistingVersion()
    {
        var (checker, cache, logger, distributedLock) = CreateFixture();
        cache.Seed(VersionCacheKey, 5L);

        await checker.InvalidateAllAsync();

        Assert.Multiple(() =>
        {
            Assert.That(cache.ReadVersion(), Is.EqualTo(6L), "全量失效应在现有版本号上 +1");
            Assert.That(distributedLock.AcquireCount, Is.EqualTo(1), "递增仍应经分布式锁保护");
            Assert.That(logger.Entries, Is.Empty, "版本号存在时不得触发重建告警");
        });

        var userId = Guid.NewGuid();
        await checker.InvalidateAsync(userId);
        Assert.That(cache.RemovedKeys.Contains(UserKey(6L, userId)), Is.True, "递增后用户权限缓存 key 应使用新版本号");
    }

    /// <summary>版本号缺失时 InvalidateAllAsync 的读-改-写仍正确：锁内只重建一次，随后一次等价递增。</summary>
    [Test]
    public async Task InvalidateAllAsync_WhenVersionMissing_RebuildsOnceThenIncrementsOnce()
    {
        var (checker, cache, logger, distributedLock) = CreateFixture();

        await checker.InvalidateAllAsync();

        Assert.Multiple(() =>
        {
            Assert.That(cache.ReadVersion(), Is.GreaterThan(1_000_000_000L), "冷启动递增仍应落在安全版本号上");
            Assert.That(distributedLock.AcquireCount, Is.EqualTo(1), "递增仍应经分布式锁保护");
            Assert.That(logger.Entries.Count(entry => entry.Level == LogLevel.Warning), Is.EqualTo(1),
                "锁内缺失时只重建一次，不得重复触发重建");
            Assert.That(cache.Writes.Count(write => write.Key == VersionCacheKey), Is.EqualTo(2),
                "一次重建写回 + 一次 +1 递增，读-改-写仍为单次等价自增");
        });
    }

    /// <summary>版本号 key 反复丢失时，重建值严格单调递增（兼容系统时钟回拨与同毫秒并发重建）。</summary>
    [Test]
    public async Task VersionKeyLostRepeatedly_RebuiltVersionsAreStrictlyIncreasing()
    {
        var (checker, cache, _, _) = CreateFixture();
        List<long> versions = [];

        for (var i = 0; i < 3; i++)
        {
            cache.Drop(VersionCacheKey);
            await checker.GetPermissionsAsync(Guid.NewGuid());
            versions.Add(cache.ReadVersion());
        }

        Assert.Multiple(() =>
        {
            Assert.That(versions, Is.All.GreaterThan(0), "重建版本号恒为正，绝不回退 0");
            Assert.That(versions[1], Is.GreaterThan(versions[0]), "第二次重建版本号必须大于第一次");
            Assert.That(versions[2], Is.GreaterThan(versions[1]), "第三次重建版本号必须大于第二次");
        });
    }

    /// <summary>版本号命中时沿用既有 key 规则，HasPermissionAsync 语义不变且不触发任何重建。</summary>
    [Test]
    public async Task VersionHit_HasPermissionAsync_UsesStoredVersionWithoutRebuild()
    {
        var userId = Guid.NewGuid();
        var (checker, cache, logger, _) = CreateFixture();
        HashSet<string> granted = ["granted:perm"];
        cache.Seed(VersionCacheKey, 42L);
        cache.Seed(UserKey(42L, userId), granted);

        var hasGranted = await checker.HasPermissionAsync(userId, "granted:perm");
        var hasOther = await checker.HasPermissionAsync(userId, "other:perm");
        var hasBlank = await checker.HasPermissionAsync(userId, " ");

        Assert.Multiple(() =>
        {
            Assert.That(hasGranted, Is.True);
            Assert.That(hasOther, Is.False);
            Assert.That(hasBlank, Is.False, "空权限标识应直接拒绝");
            Assert.That(cache.ReadKeys.Contains(UserKey(42L, userId)), Is.True, "版本号命中时应使用缓存中的版本号构造 key");
            Assert.That(cache.Writes.Any(write => write.Key == VersionCacheKey), Is.False, "版本号命中时不得回写版本 key");
            Assert.That(logger.Entries, Is.Empty, "版本号命中时不得触发重建告警");
        });
    }

    /// <summary>构造被测校验器与共享测试桩（仓储列表恒为空，权限加载直接得到空权限集合）。</summary>
    /// <returns>校验器、缓存桩、日志桩、分布式锁桩。</returns>
    private static (PermissionChecker Checker, RecordingCache Cache, CapturingLogger<PermissionChecker> Logger, RecordingLock Lock) CreateFixture()
    {
        var cache = new RecordingCache();
        var logger = new CapturingLogger<PermissionChecker>();
        var distributedLock = new RecordingLock();
        var checker = new PermissionChecker(
            new EmptyRepositoryStub<SysUserRole>(),
            new EmptyRepositoryStub<SysRole>(),
            new EmptyRepositoryStub<SysRoleMenu>(),
            new EmptyRepositoryStub<SysMenu>(),
            cache,
            distributedLock,
            logger);
        return (checker, cache, logger, distributedLock);
    }

    /// <summary>记录读写 / 删除行为的内存缓存桩（模拟 HybridCache 泛型读写契约，未命中返回 default）。</summary>
    private sealed class RecordingCache : ICache
    {
        private readonly Dictionary<string, object> _store = [];

        /// <summary>GetAsync 读取过的 key（按顺序）。</summary>
        public List<string> ReadKeys { get; } = [];

        /// <summary>SetAsync 写入记录。</summary>
        public List<(string Key, object? Value, CacheTtl? Ttl, CacheTtlProfile? Profile)> Writes { get; } = [];

        /// <summary>RemoveAsync 删除过的 key。</summary>
        public List<string> RemovedKeys { get; } = [];

        /// <inheritdoc />
        public ValueTask<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CacheTtl ttl = CacheTtl.Medium, CancellationToken cancellationToken = default) where T : notnull
        {
            if (_store.TryGetValue(key, out var existing))
            {
                return ValueTask.FromResult((T)existing);
            }

            var created = factory(cancellationToken).GetAwaiter().GetResult();
            _store[key] = created;
            return ValueTask.FromResult(created);
        }

        /// <inheritdoc />
        public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            ReadKeys.Add(key);
            return ValueTask.FromResult(_store.TryGetValue(key, out var value) ? (T?)value : default);
        }

        /// <inheritdoc />
        public Task SetAsync<T>(string key, T value, CacheTtl ttl = CacheTtl.Medium, CancellationToken cancellationToken = default)
        {
            Writes.Add((key, value, ttl, null));
            _store[key] = value!;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SetAsync<T>(string key, T value, CacheTtlProfile profile, CancellationToken cancellationToken = default)
        {
            Writes.Add((key, value, null, profile));
            _store[key] = value!;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            RemovedKeys.Add(key);
            _store.Remove(key);
            return Task.CompletedTask;
        }

        /// <summary>预置缓存条目（不记录为测试行为）。</summary>
        /// <param name="key">缓存 key。</param>
        /// <param name="value">缓存值。</param>
        public void Seed(string key, object value) => _store[key] = value;

        /// <summary>按 key 直接删除条目，模拟 Redis 重启 / flush / 淘汰导致版本号丢失。</summary>
        /// <param name="key">缓存 key。</param>
        public void Drop(string key) => _store.Remove(key);

        /// <summary>清空读取记录，便于断言某次调用后的 key 构造。</summary>
        public void ClearReadKeys() => ReadKeys.Clear();

        /// <summary>直接读取当前存储的版本号（不记录读取）。</summary>
        /// <returns>当前版本号；缺失时为 0。</returns>
        public long ReadVersion()
            => _store.TryGetValue(VersionCacheKey, out var value) && value is long number ? number : 0;
    }

    /// <summary>记录获取次数、始终返回空锁句柄的分布式锁桩。</summary>
    private sealed class RecordingLock : IDistributedLock
    {
        /// <summary>获取次数。</summary>
        public int AcquireCount { get; private set; }

        /// <inheritdoc />
        public ValueTask<IAsyncDisposable?> AcquireAsync(string key, TimeSpan expiry, TimeSpan? waitTimeout = null, CancellationToken cancellationToken = default)
        {
            AcquireCount++;
            return ValueTask.FromResult<IAsyncDisposable?>(new LockHandle());
        }

        /// <summary>空锁句柄。</summary>
        private sealed class LockHandle : IAsyncDisposable
        {
            /// <inheritdoc />
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>记录日志级别与消息的假日志器。</summary>
    /// <typeparam name="TCategory">日志类别。</typeparam>
    private sealed class CapturingLogger<TCategory> : ILogger<TCategory>
    {
        /// <summary>已记录的日志条目。</summary>
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>空仓储桩：列表查询恒返回空集合，使权限加载直接得到空权限集合。</summary>
    /// <typeparam name="TEntity">实体类型。</typeparam>
    private sealed class EmptyRepositoryStub<TEntity> : IDomainRepository<TEntity>
        where TEntity : DomainBase
    {
        /// <inheritdoc />
        public Task<TEntity?> GetAsync(IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null, CancellationToken cancellationToken = default)
            => Task.FromResult<TEntity?>(null);

        /// <inheritdoc />
        public Task<TDto?> GetAsync<TDto>(IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null, CancellationToken cancellationToken = default)
            where TDto : class, new()
            => Task.FromResult<TDto?>(null);

        /// <inheritdoc />
        public Task<List<TEntity>> GetListAsync(
            IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
            IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<List<TEntity>>([]);

        /// <inheritdoc />
        public Task<List<TDto>> GetListAsync<TDto>(
            IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null,
            IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null,
            CancellationToken cancellationToken = default)
            where TDto : class, new()
            => Task.FromResult<List<TDto>>([]);

        /// <inheritdoc />
        public Task<PagedResponse<TEntity>> GetPagedAsync(PagedRequest request, IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null, IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<PagedResponse<TDto>> GetPagedAsync<TDto>(PagedRequest request, IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null, IDictionary<Expression<Func<TEntity, object>>, bool?>? orders = null, CancellationToken cancellationToken = default)
            where TDto : class, new()
            => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> AnyAsync(IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        /// <inheritdoc />
        public Task<long> CountAsync(IEnumerable<Expression<Func<TEntity, bool>>>? predicates = null, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        /// <inheritdoc />
        public Task<bool> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        /// <inheritdoc />
        public Task<bool> InsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        /// <inheritdoc />
        public Task<bool> HardDeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
