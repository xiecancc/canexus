
using Application.Behaviors;
using Application.Contracts.Abstractions;
using Domain.Services.Users;
using Domain.Shared.Exceptions;
using Infrastructure.Shared.Caching;
using Infrastructure.Shared.Contexts;
using Infrastructure.Shared.Locking;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests.Behaviors;

/// <summary>
/// 写命令幂等管道回归：仅在携带 Idempotency-Key 时生效；重复键返回首次结果且不重复执行，
/// 同一键不同参数报冲突，无键调用完全不受影响。
/// </summary>
[TestFixture]
public class IdempotencyBehaviorTests
{
    /// <summary>测试用写命令（只需实现 ICommand 标记）。</summary>
    /// <param name="Value">请求参数（参与指纹）。</param>
    private sealed record TestCommand(string Value) : IRequest<string>, ICommand;

    /// <summary>构造幂等行为与共享桩。</summary>
    /// <param name="key">幂等键（null 表示请求未携带）。</param>
    /// <param name="cache">缓存桩实例。</param>
    /// <returns>行为实例。</returns>
    private static IdempotencyBehavior<TestCommand, string> CreateBehavior(string? key, out MemoryCacheStub cache)
    {
        cache = new MemoryCacheStub();
        return new IdempotencyBehavior<TestCommand, string>(
            cache,
            new IdempotencyContextStub(key),
            new UserContextStub(),
            new NoLockStub(),
            NullLogger<IdempotencyBehavior<TestCommand, string>>.Instance);
    }

    /// <summary>同一幂等键的第二次调用应命中缓存，处理器只执行一次。</summary>
    [Test]
    public async Task SameKey_SecondCall_ReturnsFirstResult_WithoutReexecuting()
    {
        var behavior = CreateBehavior("key-1", out _);
        var calls = 0;
        Task<string> Next() => Task.FromResult(++calls == 1 ? "first-result" : "second-result");

        var first = await behavior.Handle(new TestCommand("A"), () => Next(), CancellationToken.None);
        var second = await behavior.Handle(new TestCommand("A"), () => Next(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("first-result"));
            Assert.That(second, Is.EqualTo("first-result"), "重复提交应返回首次结果");
            Assert.That(calls, Is.EqualTo(1), "重复提交不得再次执行处理器");
        });
    }

    /// <summary>同一幂等键用于不同参数应报冲突，避免结果误复用。</summary>
    [Test]
    public void SameKey_DifferentPayload_ThrowsConflict()
    {
        var behavior = CreateBehavior("key-2", out _);
        Task<string> Next() => Task.FromResult("result");

        Assert.DoesNotThrowAsync(async () =>
            await behavior.Handle(new TestCommand("A"), () => Next(), CancellationToken.None));

        var exception = Assert.ThrowsAsync<BusinessException>(async () =>
            await behavior.Handle(new TestCommand("B"), () => Next(), CancellationToken.None));
        Assert.That(exception!.Message, Does.Contain("幂等键已用于不同的请求参数"));
    }

    /// <summary>未携带幂等键时每次都正常执行，行为对既有调用零影响。</summary>
    [Test]
    public async Task WithoutKey_AlwaysExecutes()
    {
        var behavior = CreateBehavior(null, out _);
        var calls = 0;
        Task<string> Next() => Task.FromResult("call-" + ++calls);

        var first = await behavior.Handle(new TestCommand("A"), () => Next(), CancellationToken.None);
        var second = await behavior.Handle(new TestCommand("A"), () => Next(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("call-1"));
            Assert.That(second, Is.EqualTo("call-2"));
        });
    }

    /// <summary>缓存中的值类型结果（false）必须能与「未命中」区分。</summary>
    [Test]
    public async Task CachedValueTypeResult_IsNotTreatedAsMiss()
    {
        var cache = new MemoryCacheStub();
        var behavior = new IdempotencyBehavior<TestCommand, bool>(
            cache,
            new IdempotencyContextStub("key-bool"),
            new UserContextStub(),
            new NoLockStub(),
            NullLogger<IdempotencyBehavior<TestCommand, bool>>.Instance);
        var calls = 0;
        Task<bool> Next()
        {
            calls++;
            return Task.FromResult(false);
        }

        var first = await behavior.Handle(new TestCommand("A"), () => Next(), CancellationToken.None);
        var second = await behavior.Handle(new TestCommand("A"), () => Next(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.False);
            Assert.That(second, Is.False);
            Assert.That(calls, Is.EqualTo(1), "缓存的 false 不得被当成未命中而重复执行");
        });
    }

    /// <summary>内存缓存桩（按 key 存 object，模拟 HybridCache 的泛型读写）。</summary>
    private sealed class MemoryCacheStub : ICache
    {
        private readonly Dictionary<string, object> _store = [];

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
            => ValueTask.FromResult(_store.TryGetValue(key, out var value) ? (T?)value : default);

        /// <inheritdoc />
        public Task SetAsync<T>(string key, T value, CacheTtl ttl = CacheTtl.Medium, CancellationToken cancellationToken = default)
        {
            _store[key] = value!;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SetAsync<T>(string key, T value, CacheTtlProfile profile, CancellationToken cancellationToken = default)
        {
            _store[key] = value!;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }
    }

    /// <summary>幂等键桩。</summary>
    /// <param name="key">固定返回的键。</param>
    private sealed class IdempotencyContextStub(string? key) : IIdempotencyContext
    {
        /// <inheritdoc />
        public string? Key => key;
    }

    /// <summary>用户上下文桩。</summary>
    private sealed class UserContextStub : IUserContext
    {
        /// <inheritdoc />
        public Guid? UserId { get; } = Guid.NewGuid();

        /// <inheritdoc />
        public string? UserName => "tester";

        /// <inheritdoc />
        public Guid? DeptId => null;

        /// <inheritdoc />
        public IReadOnlyList<Guid> RoleIds => [];

        /// <inheritdoc />
        public IReadOnlyList<string> Roles => [];

        /// <inheritdoc />
        public Guid? TenantId => null;

        /// <inheritdoc />
        public bool IsAuthenticated => true;

        /// <inheritdoc />
        public bool IsSuperAdmin => false;
    }

    /// <summary>无锁桩（验证拿不到锁时降级路径仍由缓存保证语义）。</summary>
    private sealed class NoLockStub : IDistributedLock
    {
        /// <inheritdoc />
        public ValueTask<IAsyncDisposable?> AcquireAsync(string key, TimeSpan expiry, TimeSpan? waitTimeout = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IAsyncDisposable?>(null);
    }
}
