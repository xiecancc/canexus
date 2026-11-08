using Application.Events;
using Domain.Entities;
using Domain.Events;
using Domain.Services.Rules;

namespace Application.Tests.Events;

/// <summary>
/// 参数 / 字典缓存键的租户隔离单元测试：平台键沿用历史格式，租户键互不相干，
/// 租户级写事件同时失效平台视图键与该租户的隔离键（Outbox 重投不依赖请求上下文）。
/// </summary>
[TestFixture]
public class TenantScopedCacheKeyTests
{
    private const string ConfigKey = "sys.user.initPassword";
    private const string DictType = "sys_normal_disable";

    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    /// <summary>平台范围键沿用历史格式，保证既有失效逻辑与缓存数据兼容。</summary>
    [Test]
    public void ValueCacheKey_PlatformScope_KeepsLegacyFormat()
    {
        Assert.That(ConfigRules.ValueCacheKey(ConfigKey), Is.EqualTo("config:key:" + ConfigKey));
        Assert.That(DictRules.DataTypeCacheKey(DictType), Is.EqualTo("dict:data:" + DictType));
    }

    /// <summary>不同租户的同名参数 / 字典键不得互相命中。</summary>
    [Test]
    public void CacheKeys_DifferentTenants_DoNotCollide()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConfigRules.ValueCacheKey(ConfigKey, TenantA),
                Is.Not.EqualTo(ConfigRules.ValueCacheKey(ConfigKey, TenantB)));
            Assert.That(ConfigRules.ValueCacheKey(ConfigKey, TenantA),
                Is.Not.EqualTo(ConfigRules.ValueCacheKey(ConfigKey)));
            Assert.That(DictRules.DataTypeCacheKey(DictType, TenantA),
                Is.Not.EqualTo(DictRules.DataTypeCacheKey(DictType, TenantB)));
        });
    }

    /// <summary>租户级参数写事件：平台键与该租户键同时失效。</summary>
    [Test]
    public async Task ConfigCreated_TenantScoped_InvalidatesPlatformAndTenantKeys()
    {
        var cache = new RecordingCache();
        var handler = new ConfigCacheInvalidationHandler(cache);
        var config = new SysConfig { Id = Guid.NewGuid(), ConfigKey = ConfigKey, TenantId = TenantA };

        await handler.Handle(new DomainEventNotification(new ConfigCreatedEvent(config)), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.EqualTo(new[]
        {
            ConfigRules.ValueCacheKey(ConfigKey),
            ConfigRules.ValueCacheKey(ConfigKey, TenantA)
        }));
    }

    /// <summary>租户级字典写事件：平台键与该租户键同时失效。</summary>
    [Test]
    public async Task DictDataCreated_TenantScoped_InvalidatesPlatformAndTenantKeys()
    {
        var cache = new RecordingCache();
        var handler = new DictCacheInvalidationHandler(cache);
        var data = new SysDictData { Id = Guid.NewGuid(), DictType = DictType, TenantId = TenantB };

        await handler.Handle(new DomainEventNotification(new DictDataCreatedEvent(data)), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.EqualTo(new[]
        {
            DictRules.DataTypeCacheKey(DictType),
            DictRules.DataTypeCacheKey(DictType, TenantB)
        }));
    }
}
