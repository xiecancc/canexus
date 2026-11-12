
using System.Reflection;
using Infrastructure.Caching;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Tests;
/// <summary>
/// CacheMonitorProvider 降级与可重试回归（P2-8）：
/// Redis 未启用 / 连接失败时返回未连接快照且不抛异常；连接失败不缓存永久降级状态，后续请求可重试。
/// </summary>
[TestFixture]
public class CacheMonitorProviderTests
{
    /// <summary>Redis 未启用时直接返回未连接降级快照，不建立任何连接。</summary>
    [Test]
    public async Task RedisDisabled_ReturnsDisconnectedSnapshot()
    {
        var provider = new CacheMonitorProvider(new StubOptionsMonitor<CacheOption>(
            new CacheOption { RedisEnabled = false }));

        var snapshot = await provider.GetSnapshotAsync();

        Assert.That(snapshot.Connected, Is.False);
    }

    /// <summary>连接参数构造失败时返回降级快照且不抛异常（监控端点须始终可响应）。</summary>
    [Test]
    public async Task InvalidRedisConfiguration_ReturnsDisconnected_WithoutThrowing()
    {
        // 空白连接串令 RedisConnectionOptions.Create 抛 ArgumentException，强制走失败降级路径
        var provider = new CacheMonitorProvider(new StubOptionsMonitor<CacheOption>(
            new CacheOption { RedisEnabled = true, RedisConfiguration = " " }));

        var snapshot = await provider.GetSnapshotAsync();

        Assert.That(snapshot.Connected, Is.False);
    }

    /// <summary>连接失败不缓存永久降级状态（去掉首次失败即永久降级），后续监控请求可重试。</summary>
    [Test]
    public async Task ConnectionFailure_DoesNotLatchPermanently_AllowsRetry()
    {
        var provider = new CacheMonitorProvider(new StubOptionsMonitor<CacheOption>(
            new CacheOption { RedisEnabled = true, RedisConfiguration = " " }));

        var first = await provider.GetSnapshotAsync();
        Assert.That(first.Connected, Is.False);

        // 关键回归：失败后不得把「连接为 null」缓存为永久状态（旧实现用 Lazy + _initialized=true 锁死）；
        // 连接字段应保持 null，使后续请求重新尝试建连。
        var connectionField = typeof(CacheMonitorProvider).GetField("_connection",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(connectionField, Is.Not.Null, "应存在懒加载连接字段");
        Assert.That(connectionField!.GetValue(provider), Is.Null, "连接失败后连接字段应保持 null（可重试）");

        var second = await provider.GetSnapshotAsync();
        Assert.That(second.Connected, Is.False, "第二次请求仍返回降级快照且不抛异常");
    }

    /// <summary>测试用 IOptionsMonitor 桩。</summary>
    /// <typeparam name="T">选项类型。</typeparam>
    /// <param name="value">固定返回的选项值。</param>
    private sealed class StubOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        /// <inheritdoc />
        public T CurrentValue => value;

        /// <inheritdoc />
        public T Get(string? name) => value;

        /// <inheritdoc />
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
