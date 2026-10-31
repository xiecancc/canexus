namespace Application.Tests.Events;

using Application.Events;
using Application.Permissions;
using Domain.Events;
using Domain.Services.Rules;
using Domain.Shared.Enums;
using Infrastructure.Shared.Caching;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>用于测试的通知发布器：记录被发布的 MediatR 通知。</summary>
public class RecordingPublisher : IPublisher
{
    /// <summary>已发布的通知集合（按发布顺序）。</summary>
    public List<object> Published { get; } = [];

    /// <inheritdoc />
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        Published.Add(notification);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        Published.Add(notification);
        return Task.CompletedTask;
    }
}

/// <summary>用于测试的权限校验器：记录缓存失效调用。</summary>
public class RecordingPermissionChecker : IPermissionChecker
{
    /// <summary>已失效的用户 Id 集合。</summary>
    public List<Guid> InvalidatedUserIds { get; } = [];

    /// <summary>全量失效调用次数。</summary>
    public int InvalidateAllCallCount { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    /// <inheritdoc />
    public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task InvalidateAsync(Guid userId)
    {
        InvalidatedUserIds.Add(userId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InvalidateAllAsync()
    {
        InvalidateAllCallCount++;
        return Task.CompletedTask;
    }
}

/// <summary>用于测试的应用缓存：记录被移除的缓存键。</summary>
public class RecordingCache : ICache
{
    /// <summary>已移除的缓存键（按移除顺序）。</summary>
    public List<string> RemovedKeys { get; } = [];

    /// <inheritdoc />
    public ValueTask<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CacheTtl ttl = CacheTtl.Medium, CancellationToken cancellationToken = default) where T : notnull
    {
        return factory(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(default(T));
    }

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value, CacheTtl ttl = CacheTtl.Medium, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value, CacheTtlProfile profile, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        RemovedKeys.Add(key);
        return Task.CompletedTask;
    }
}

/// <summary>领域事件发布器单元测试：领域事件经非泛型通知信封发布给监听器。</summary>
[TestFixture]
public class MediatRDomainEventPublisherTests
{
    private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Test]
    public async Task PublishAsync_WrapsDomainEventIntoNotification()
    {
        // 领域事件被包装为非泛型通知信封后经 MediatR 发布
        var publisher = new RecordingPublisher();
        var domainEventPublisher = new MediatRDomainEventPublisher(publisher);
        var domainEvent = new UserChangedEvent(UserId, AggregateChangeType.Updated);

        await domainEventPublisher.PublishAsync(domainEvent);

        Assert.Multiple(() =>
        {
            Assert.That(publisher.Published, Has.Count.EqualTo(1));
            Assert.That(publisher.Published[0], Is.InstanceOf<DomainEventNotification>());
            var notification = (DomainEventNotification)publisher.Published[0];
            Assert.That(notification.DomainEvent, Is.SameAs(domainEvent));
        });
    }
}

/// <summary>权限缓存失效监听器单元测试：按事件类型触发对应的缓存清理口径。</summary>
[TestFixture]
public class PermissionCacheInvalidationHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid RoleId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid MenuId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    [Test]
    public async Task Handle_UserChangedEvent_InvalidatesSingleUser()
    {
        // 用户变更：仅失效该用户的权限缓存
        var permissionChecker = new RecordingPermissionChecker();
        var handler = new PermissionCacheInvalidationHandler(permissionChecker);

        await handler.Handle(new DomainEventNotification(
            new UserChangedEvent(UserId, AggregateChangeType.Updated)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(permissionChecker.InvalidatedUserIds, Is.EqualTo([UserId]));
            Assert.That(permissionChecker.InvalidateAllCallCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Handle_UserPasswordResetEvent_InvalidatesSingleUser()
    {
        // 重置密码：同样失效该用户的权限缓存
        var permissionChecker = new RecordingPermissionChecker();
        var handler = new PermissionCacheInvalidationHandler(permissionChecker);

        await handler.Handle(new DomainEventNotification(
            new UserChangedEvent(UserId, AggregateChangeType.PasswordReset)), CancellationToken.None);

        Assert.That(permissionChecker.InvalidatedUserIds, Is.EqualTo([UserId]));
    }

    [Test]
    public async Task Handle_RoleChangedEvent_InvalidatesAllPermissions()
    {
        // 角色变更：影响任意持有该角色的用户，全量失效
        var permissionChecker = new RecordingPermissionChecker();
        var handler = new PermissionCacheInvalidationHandler(permissionChecker);

        await handler.Handle(new DomainEventNotification(
            new RoleChangedEvent(RoleId, AggregateChangeType.Deleted)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(permissionChecker.InvalidatedUserIds, Is.Empty);
            Assert.That(permissionChecker.InvalidateAllCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Handle_MenuChangedEvent_InvalidatesAllPermissions()
    {
        // 菜单变更：影响任意已授权角色的用户，全量失效
        var permissionChecker = new RecordingPermissionChecker();
        var handler = new PermissionCacheInvalidationHandler(permissionChecker);

        await handler.Handle(new DomainEventNotification(
            new MenuChangedEvent(MenuId, AggregateChangeType.Created)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(permissionChecker.InvalidatedUserIds, Is.Empty);
            Assert.That(permissionChecker.InvalidateAllCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Handle_UnrelatedEvent_DoesNothing()
    {
        // 非权限领域事件（如定时任务变更）：不触发任何权限缓存清理
        var permissionChecker = new RecordingPermissionChecker();
        var handler = new PermissionCacheInvalidationHandler(permissionChecker);

        await handler.Handle(new DomainEventNotification(
            new JobChangedEvent(JobId, AggregateChangeType.Updated)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(permissionChecker.InvalidatedUserIds, Is.Empty);
            Assert.That(permissionChecker.InvalidateAllCallCount, Is.EqualTo(0));
        });
    }
}

/// <summary>字典缓存失效监听器单元测试。</summary>
[TestFixture]
public class DictCacheInvalidationHandlerTests
{
    private const string DictType = "sys_normal_disable";
    private static readonly Guid DictDataId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Test]
    public async Task Handle_DictDataChangedEvent_InvalidatesTypeCache()
    {
        var cache = new RecordingCache();
        var handler = new DictCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new DictDataChangedEvent(DictDataId, DictType, AggregateChangeType.Created)), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.EqualTo([DictRules.DataTypeCacheKey(DictType)]));
    }

    [Test]
    public async Task Handle_DictTypeMigrated_InvalidatesBothCaches()
    {
        // 类型迁移：新旧类型缓存均失效
        var cache = new RecordingCache();
        var handler = new DictCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new DictDataChangedEvent(DictDataId, DictType, AggregateChangeType.Updated, "old_type")), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.EqualTo([DictRules.DataTypeCacheKey(DictType), DictRules.DataTypeCacheKey("old_type")]));
    }

    [Test]
    public async Task Handle_UnrelatedEvent_DoesNothing()
    {
        // 非字典领域事件：不触发缓存清理
        var cache = new RecordingCache();
        var handler = new DictCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new UserChangedEvent(DictDataId, AggregateChangeType.Updated)), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.Empty);
    }
}

/// <summary>参数配置缓存失效监听器单元测试。</summary>
[TestFixture]
public class ConfigCacheInvalidationHandlerTests
{
    private const string ConfigKey = "sys.account.captchaEnabled";
    private static readonly Guid ConfigId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Test]
    public async Task Handle_ConfigChangedEvent_InvalidatesKeyCache()
    {
        var cache = new RecordingCache();
        var handler = new ConfigCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new ConfigChangedEvent(ConfigId, ConfigKey, AggregateChangeType.Deleted)), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.EqualTo([ConfigRules.ValueCacheKey(ConfigKey)]));
    }

    [Test]
    public async Task Handle_ConfigKeyRenamed_InvalidatesBothCaches()
    {
        // 键名变更：新旧键缓存均失效
        var cache = new RecordingCache();
        var handler = new ConfigCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new ConfigChangedEvent(ConfigId, ConfigKey, AggregateChangeType.Updated, "old_key")), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.EqualTo([ConfigRules.ValueCacheKey(ConfigKey), ConfigRules.ValueCacheKey("old_key")]));
    }

    [Test]
    public async Task Handle_UnrelatedEvent_DoesNothing()
    {
        // 非配置领域事件：不触发缓存清理
        var cache = new RecordingCache();
        var handler = new ConfigCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new UserChangedEvent(ConfigId, AggregateChangeType.Updated)), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.Empty);
    }
}

/// <summary>领域事件审计日志监听器单元测试。</summary>
[TestFixture]
public class DomainEventLoggingHandlerTests
{
    [Test]
    public async Task Handle_AnyEvent_CompletesWithoutError()
    {
        // 通用监听器：任意领域事件均可安全记录
        var handler = new DomainEventLoggingHandler(NullLogger<DomainEventLoggingHandler>.Instance);

        Assert.DoesNotThrowAsync(async () => await handler.Handle(
            new DomainEventNotification(new UserChangedEvent(Guid.NewGuid(), AggregateChangeType.Created)),
            CancellationToken.None));
    }
}
