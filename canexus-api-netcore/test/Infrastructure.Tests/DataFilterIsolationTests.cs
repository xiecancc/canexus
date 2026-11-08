using Infrastructure.Filtering;

namespace Infrastructure.Tests;

/// <summary>数据过滤开关的异步上下文隔离测试：单例服务不得让一个请求关闭过滤影响并发请求。</summary>
[TestFixture]
public class DataFilterIsolationTests
{
    /// <summary>两个并行异步上下文各自设置开关，互不覆盖；未设置时回落到默认启用。</summary>
    [Test]
    public async Task SoftDeleteOverride_IsIsolatedBetweenAsyncContexts()
    {
        var filter = new DataFilter { SoftDeleteEnabled = true };
        using var barrier = new Barrier(2);

        var closed = Task.Run(() =>
        {
            filter.SoftDeleteEnabled = false;
            barrier.SignalAndWait();
            return filter.SoftDeleteEnabled;
        });
        var opened = Task.Run(() =>
        {
            filter.SoftDeleteEnabled = true;
            barrier.SignalAndWait();
            return filter.SoftDeleteEnabled;
        });

        var results = await Task.WhenAll(closed, opened);
        Assert.Multiple(() =>
        {
            Assert.That(results[0], Is.False, "关闭过滤的上下文必须保持关闭");
            Assert.That(results[1], Is.True, "另一并发上下文不得被连带关闭");
            Assert.That(filter.SoftDeleteEnabled, Is.True, "父上下文的开关不得被并发上下文改写");
        });
    }

    /// <summary>租户过滤开关同样按异步上下文隔离。</summary>
    [Test]
    public async Task TenantFilterOverride_IsIsolatedBetweenAsyncContexts()
    {
        var filter = new DataFilter { TenantFilterEnabled = true };
        using var barrier = new Barrier(2);

        var closed = Task.Run(() =>
        {
            filter.TenantFilterEnabled = false;
            barrier.SignalAndWait();
            return filter.TenantFilterEnabled;
        });
        var opened = Task.Run(() =>
        {
            filter.TenantFilterEnabled = true;
            barrier.SignalAndWait();
            return filter.TenantFilterEnabled;
        });

        var results = await Task.WhenAll(closed, opened);
        Assert.Multiple(() =>
        {
            Assert.That(results[0], Is.False);
            Assert.That(results[1], Is.True);
            Assert.That(filter.TenantFilterEnabled, Is.True, "父上下文的开关不得被并发上下文改写");
        });
    }
}
