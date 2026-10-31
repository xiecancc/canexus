namespace Application.Tests.Events;

using Application.Events;
using Application.Permissions;
using Domain.Events;
using Domain.Shared.Enums;
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

/// <summary>领域事件发布器单元测试：领域事件经通知信封发布给监听器。</summary>
[TestFixture]
public class MediatRDomainEventPublisherTests
{
    private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Test]
    public async Task PublishAsync_WrapsDomainEventIntoNotification()
    {
        // 领域事件被包装为强类型通知信封后经 MediatR 发布
        var publisher = new RecordingPublisher();
        var domainEventPublisher = new MediatRDomainEventPublisher(publisher);
        var domainEvent = new UserChangedEvent(UserId, AggregateChangeType.Updated);

        await domainEventPublisher.PublishAsync(domainEvent);

        Assert.Multiple(() =>
        {
            Assert.That(publisher.Published, Has.Count.EqualTo(1));
            Assert.That(publisher.Published[0], Is.InstanceOf<DomainEventNotification<UserChangedEvent>>());
            var notification = (DomainEventNotification<UserChangedEvent>)publisher.Published[0];
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

    [Test]
    public async Task Handle_UserChangedEvent_InvalidatesSingleUser()
    {
        // 用户变更：仅失效该用户的权限缓存
        var permissionChecker = new RecordingPermissionChecker();
        var handler = new PermissionCacheInvalidationHandler(permissionChecker, NullLogger<PermissionCacheInvalidationHandler>.Instance);

        await handler.Handle(new DomainEventNotification<UserChangedEvent>(
            new UserChangedEvent(UserId, AggregateChangeType.Updated)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(permissionChecker.InvalidatedUserIds, Is.EqualTo([UserId]));
            Assert.That(permissionChecker.InvalidateAllCallCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Handle_RoleChangedEvent_InvalidatesAllPermissions()
    {
        // 角色变更：影响任意持有该角色的用户，全量失效
        var permissionChecker = new RecordingPermissionChecker();
        var handler = new PermissionCacheInvalidationHandler(permissionChecker, NullLogger<PermissionCacheInvalidationHandler>.Instance);

        await handler.Handle(new DomainEventNotification<RoleChangedEvent>(
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
        var handler = new PermissionCacheInvalidationHandler(permissionChecker, NullLogger<PermissionCacheInvalidationHandler>.Instance);

        await handler.Handle(new DomainEventNotification<MenuChangedEvent>(
            new MenuChangedEvent(MenuId, AggregateChangeType.Created)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(permissionChecker.InvalidatedUserIds, Is.Empty);
            Assert.That(permissionChecker.InvalidateAllCallCount, Is.EqualTo(1));
        });
    }
}
