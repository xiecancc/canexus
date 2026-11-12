
using Application.Behaviors;
using Application.Contracts.Abstractions;
using Domain.Services.Users;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
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
            new AcquiredLockStub(),
            NullLogger<IdempotencyBehavior<TestCommand, string>>.Instance);
    }

    /// <summary>同一幂等键的第二次调用应命中缓存，处理器只执行一次。</summary>
    [Test]
    public async Task SameKey_SecondCall_ReturnsFirstResult_WithoutReexecuting()
    {
        var behavior = CreateBehavior("key-1", out _);
        var calls = 0;
        Task<string> Next() => Task.FromResult(++calls == 1 ? "first-result" : "second-result");

        var first = await behavior.Handle(new TestCommand("A"), _ => Next(), CancellationToken.None);
        var second = await behavior.Handle(new TestCommand("A"), _ => Next(), CancellationToken.None);

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
            await behavior.Handle(new TestCommand("A"), _ => Next(), CancellationToken.None));

        var exception = Assert.ThrowsAsync<BusinessException>(async () =>
            await behavior.Handle(new TestCommand("B"), _ => Next(), CancellationToken.None));
        Assert.That(exception!.Message, Does.Contain("幂等键已用于不同的请求参数"));
    }

    /// <summary>未携带幂等键时每次都正常执行，行为对既有调用零影响。</summary>
    [Test]
    public async Task WithoutKey_AlwaysExecutes()
    {
        var behavior = CreateBehavior(null, out _);
        var calls = 0;
        Task<string> Next() => Task.FromResult("call-" + ++calls);

        var first = await behavior.Handle(new TestCommand("A"), _ => Next(), CancellationToken.None);
        var second = await behavior.Handle(new TestCommand("A"), _ => Next(), CancellationToken.None);

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
            new AcquiredLockStub(),
            NullLogger<IdempotencyBehavior<TestCommand, bool>>.Instance);
        var calls = 0;
        Task<bool> Next()
        {
            calls++;
            return Task.FromResult(false);
        }

        var first = await behavior.Handle(new TestCommand("A"), _ => Next(), CancellationToken.None);
        var second = await behavior.Handle(new TestCommand("A"), _ => Next(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.False);
            Assert.That(second, Is.False);
            Assert.That(calls, Is.EqualTo(1), "缓存的 false 不得被当成未命中而重复执行");
        });
    }

    /// <summary>锁不可用（Redis 故障 / 抢占超时）时必须拒绝，不得降级为无锁执行造成并发双写。</summary>
    [Test]
    public void LockUnavailable_RejectsInsteadOfExecutingWithoutLock()
    {
        var behavior = new IdempotencyBehavior<TestCommand, string>(
            new MemoryCacheStub(),
            new IdempotencyContextStub("key-no-lock"),
            new UserContextStub(),
            new UnavailableLockStub(),
            NullLogger<IdempotencyBehavior<TestCommand, string>>.Instance);
        var calls = 0;
        Task<string> Next() => Task.FromResult("call-" + ++calls);

        var exception = Assert.ThrowsAsync<BusinessException>(async () =>
            await behavior.Handle(new TestCommand("A"), _ => Next(), CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Code, Is.EqualTo(ErrorCodes.CommonConcurrencyConflict));
            Assert.That(calls, Is.Zero, "拿不到锁时不得执行处理器");
        });
    }

    /// <summary>锁过期后被第二请求抢到时，in-flight 占位应阻止重复执行。</summary>
    [Test]
    public void InFlightPlaceholder_RejectsConcurrentExecution()
    {
        const string key = "key-inflight";
        var userId = Guid.NewGuid();
        var cache = new MemoryCacheStub();
        var behavior = new IdempotencyBehavior<TestCommand, string>(
            cache,
            new IdempotencyContextStub(key),
            new UserContextStub(userId),
            new AcquiredLockStub(),
            NullLogger<IdempotencyBehavior<TestCommand, string>>.Instance);

        // 按行为内部的键规则预置「处理中」占位，模拟首请求仍在执行、其锁已过期
        var cacheKey = $"idempotency:{userId}:{nameof(TestCommand)}:{key}";
        cache.SetAsync(
            cacheKey,
            new IdempotencyEnvelope<string> { InFlight = true },
            CacheTtlProfile.Uniform(TimeSpan.FromMinutes(2)),
            CancellationToken.None).GetAwaiter().GetResult();

        var calls = 0;
        Task<string> Next() => Task.FromResult("call-" + ++calls);
        var exception = Assert.ThrowsAsync<BusinessException>(async () =>
            await behavior.Handle(new TestCommand("A"), _ => Next(), CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Code, Is.EqualTo(ErrorCodes.CommonConcurrencyConflict));
            Assert.That(calls, Is.Zero, "命中处理中占位时不得执行处理器，避免并发双写");
        });
    }

    /// <summary>失败路径必须清除 in-flight 占位，允许同键重试。</summary>
    [Test]
    public void HandlerFailure_RemovesInFlightPlaceholder()
    {
        var cache = new MemoryCacheStub();
        var context = new IdempotencyContextStub("key-failure");
        var behavior = new IdempotencyBehavior<TestCommand, string>(
            cache, context, new UserContextStub(), new AcquiredLockStub(),
            NullLogger<IdempotencyBehavior<TestCommand, string>>.Instance);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new TestCommand("A"),
                _ => throw new InvalidOperationException("boom"), CancellationToken.None));

        // 同键重试应能再次进入处理器（占位已清），而不是被 40005 拒绝
        var calls = 0;
        Assert.DoesNotThrowAsync(async () =>
            await behavior.Handle(new TestCommand("A"),
                _ => Task.FromResult("retry-" + ++calls), CancellationToken.None));
        Assert.That(calls, Is.EqualTo(1));
    }

    /// <summary>敏感响应（含令牌）整条幂等路径跳过：不写缓存、不复用首次结果。</summary>
    [Test]
    public async Task SensitiveResult_NeverCachedOrReplayed()
    {
        var cache = new MemoryCacheStub();
        var behavior = new IdempotencyBehavior<SensitiveCommand, SensitiveResult>(
            cache,
            new IdempotencyContextStub("key-sensitive"),
            new UserContextStub(),
            new AcquiredLockStub(),
            NullLogger<IdempotencyBehavior<SensitiveCommand, SensitiveResult>>.Instance);
        var calls = 0;
        Task<SensitiveResult> Next() => Task.FromResult(new SensitiveResult { Token = "token-" + ++calls });

        var first = await behavior.Handle(new SensitiveCommand(), _ => Next(), CancellationToken.None);
        var second = await behavior.Handle(new SensitiveCommand(), _ => Next(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.Token, Is.EqualTo("token-1"));
            Assert.That(second.Token, Is.EqualTo("token-2"), "含令牌的响应不得复用首次结果");
            Assert.That(cache.WriteCount, Is.Zero, "敏感响应不得写入缓存");
        });
    }

    /// <summary>实现敏感结果标记的测试响应。</summary>
    private sealed class SensitiveResult : ISensitiveResult
    {
        /// <summary>模拟访问令牌。</summary>
        public string Token { get; set; } = string.Empty;
    }

    /// <summary>返回敏感结果的测试命令。</summary>
    private sealed record SensitiveCommand() : IRequest<SensitiveResult>, ICommand;

    /// <summary>内存缓存桩（按 key 存 object，模拟 HybridCache 的泛型读写）。</summary>
    private sealed class MemoryCacheStub : ICache
    {
        private readonly Dictionary<string, object> _store = [];

        /// <summary>写入次数（用于断言敏感响应未落缓存）。</summary>
        public int WriteCount { get; private set; }

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
            WriteCount++;
            _store[key] = value!;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SetAsync<T>(string key, T value, CacheTtlProfile profile, CancellationToken cancellationToken = default)
        {
            WriteCount++;
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
    /// <param name="userId">指定用户 Id（用于精确构造幂等缓存键）；为 null 时随机生成。</param>
    private sealed class UserContextStub(Guid? userId = null) : IUserContext
    {
        /// <inheritdoc />
        public Guid? UserId { get; } = userId ?? Guid.NewGuid();

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

    /// <summary>可获取锁桩：模拟正常路径（锁句柄释放为空操作）。</summary>
    private sealed class AcquiredLockStub : IDistributedLock
    {
        /// <inheritdoc />
        public ValueTask<IAsyncDisposable?> AcquireAsync(string key, TimeSpan expiry, TimeSpan? waitTimeout = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IAsyncDisposable?>(new LockHandle());

        /// <summary>空锁句柄。</summary>
        private sealed class LockHandle : IAsyncDisposable
        {
            /// <inheritdoc />
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>锁不可用桩：模拟 Redis 故障 / 抢占超时返回 null。</summary>
    private sealed class UnavailableLockStub : IDistributedLock
    {
        /// <inheritdoc />
        public ValueTask<IAsyncDisposable?> AcquireAsync(string key, TimeSpan expiry, TimeSpan? waitTimeout = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IAsyncDisposable?>(null);
    }
}
