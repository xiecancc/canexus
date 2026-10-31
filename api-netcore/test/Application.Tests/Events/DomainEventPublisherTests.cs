namespace Application.Tests.Events;

using Application.Events;
using Application.Permissions;
using Domain.Entities;
using Domain.Events;
using Domain.Services.Rules;
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
        var domainEvent = new UserPasswordResetEvent(UserId);

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

    private static SysUser CreateUser() => new() { Id = UserId, UserName = "user" };

    [Test]
    public async Task Handle_UserEvents_InvalidatesSingleUser()
    {
        // 用户各类事件（创建 / 修改 / 角色重分配 / 重置密码 / 删除）：仅失效该用户的权限缓存
        var permissionChecker = new RecordingPermissionChecker();
        var handler = new PermissionCacheInvalidationHandler(permissionChecker);

        await handler.Handle(new DomainEventNotification(new UserCreatedEvent(CreateUser())), CancellationToken.None);
        await handler.Handle(new DomainEventNotification(new UserUpdatedEvent(CreateUser(), CreateUser())), CancellationToken.None);
        await handler.Handle(new DomainEventNotification(new UserRolesUpdatedEvent(UserId, [], [])), CancellationToken.None);
        await handler.Handle(new DomainEventNotification(new UserPasswordResetEvent(UserId)), CancellationToken.None);
        await handler.Handle(new DomainEventNotification(new UserDeletedEvent(CreateUser())), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(permissionChecker.InvalidatedUserIds, Has.Count.EqualTo(5));
            Assert.That(permissionChecker.InvalidatedUserIds, Has.All.EqualTo(UserId));
            Assert.That(permissionChecker.InvalidateAllCallCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Handle_RoleAndMenuEvents_InvalidatesAllPermissions()
    {
        // 角色 / 菜单各类事件：影响任意持有（已授权）用户的权限，全量失效
        var permissionChecker = new RecordingPermissionChecker();
        var handler = new PermissionCacheInvalidationHandler(permissionChecker);
        var role = new SysRole { Id = RoleId };
        var menu = new SysMenu { Id = MenuId };

        await handler.Handle(new DomainEventNotification(new RoleCreatedEvent(role)), CancellationToken.None);
        await handler.Handle(new DomainEventNotification(new RoleUpdatedEvent(role, role)), CancellationToken.None);
        await handler.Handle(new DomainEventNotification(new RoleDeletedEvent(role)), CancellationToken.None);
        await handler.Handle(new DomainEventNotification(new MenuCreatedEvent(menu)), CancellationToken.None);
        await handler.Handle(new DomainEventNotification(new MenuUpdatedEvent(menu, menu)), CancellationToken.None);
        await handler.Handle(new DomainEventNotification(new MenuDeletedEvent(menu)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(permissionChecker.InvalidatedUserIds, Is.Empty);
            Assert.That(permissionChecker.InvalidateAllCallCount, Is.EqualTo(6));
        });
    }

    [Test]
    public async Task Handle_UnrelatedEvent_DoesNothing()
    {
        // 非权限领域事件（如定时任务变更）：不触发任何权限缓存清理
        var permissionChecker = new RecordingPermissionChecker();
        var handler = new PermissionCacheInvalidationHandler(permissionChecker);

        await handler.Handle(new DomainEventNotification(
            new JobUpdatedEvent(new SysJob { Id = JobId }, new SysJob { Id = JobId })), CancellationToken.None);

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

    private static SysDictData CreateDictData(string dictType) => new() { Id = DictDataId, DictType = dictType };

    [Test]
    public async Task Handle_DictDataCreatedEvent_InvalidatesTypeCache()
    {
        var cache = new RecordingCache();
        var handler = new DictCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new DictDataCreatedEvent(CreateDictData(DictType))), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.EqualTo([DictRules.DataTypeCacheKey(DictType)]));
    }

    [Test]
    public async Task Handle_DictTypeMigrated_InvalidatesBothCaches()
    {
        // 类型迁移：旧类型由 Before.DictType 表达，新旧类型缓存均失效
        var cache = new RecordingCache();
        var handler = new DictCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new DictDataUpdatedEvent(CreateDictData("old_type"), CreateDictData(DictType))), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.EqualTo([DictRules.DataTypeCacheKey(DictType), DictRules.DataTypeCacheKey("old_type")]));
    }

    [Test]
    public async Task Handle_DictDataDeletedEvent_InvalidatesTypeCache()
    {
        var cache = new RecordingCache();
        var handler = new DictCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new DictDataDeletedEvent(CreateDictData(DictType))), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.EqualTo([DictRules.DataTypeCacheKey(DictType)]));
    }

    [Test]
    public async Task Handle_UnrelatedEvent_DoesNothing()
    {
        // 非字典领域事件：不触发缓存清理
        var cache = new RecordingCache();
        var handler = new DictCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new UserPasswordResetEvent(DictDataId)), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.Empty);
    }
}

/// <summary>参数配置缓存失效监听器单元测试。</summary>
[TestFixture]
public class ConfigCacheInvalidationHandlerTests
{
    private const string ConfigKey = "sys.account.captchaEnabled";
    private static readonly Guid ConfigId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static SysConfig CreateConfig(string configKey) => new() { Id = ConfigId, ConfigKey = configKey };

    [Test]
    public async Task Handle_ConfigCreatedEvent_InvalidatesKeyCache()
    {
        var cache = new RecordingCache();
        var handler = new ConfigCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new ConfigCreatedEvent(CreateConfig(ConfigKey))), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.EqualTo([ConfigRules.ValueCacheKey(ConfigKey)]));
    }

    [Test]
    public async Task Handle_ConfigKeyRenamed_InvalidatesBothCaches()
    {
        // 键名变更：旧键名由 Before.ConfigKey 表达，新旧键缓存均失效
        var cache = new RecordingCache();
        var handler = new ConfigCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new ConfigUpdatedEvent(CreateConfig("old_key"), CreateConfig(ConfigKey))), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.EqualTo([ConfigRules.ValueCacheKey(ConfigKey), ConfigRules.ValueCacheKey("old_key")]));
    }

    [Test]
    public async Task Handle_ConfigDeletedEvent_InvalidatesKeyCache()
    {
        var cache = new RecordingCache();
        var handler = new ConfigCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new ConfigDeletedEvent(CreateConfig(ConfigKey))), CancellationToken.None);

        Assert.That(cache.RemovedKeys, Is.EqualTo([ConfigRules.ValueCacheKey(ConfigKey)]));
    }

    [Test]
    public async Task Handle_UnrelatedEvent_DoesNothing()
    {
        // 非配置领域事件：不触发缓存清理
        var cache = new RecordingCache();
        var handler = new ConfigCacheInvalidationHandler(cache);

        await handler.Handle(new DomainEventNotification(
            new UserPasswordResetEvent(ConfigId)), CancellationToken.None);

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
        // 通用监听器（TEvent = DomainEvent 全量订阅）：任意领域事件均可安全记录
        var handler = new DomainEventLoggingHandler(NullLogger<DomainEventLoggingHandler>.Instance);

        Assert.DoesNotThrowAsync(async () => await handler.Handle(
            new DomainEventNotification(new UserPasswordResetEvent(Guid.NewGuid())),
            CancellationToken.None));
    }
}
