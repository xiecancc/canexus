using System.Text.Json;
using Application.Events;
using Domain.Events;
using Domain.Shared.Json;

namespace Application.Tests.Events;

/// <summary>Outbox 载荷序列化的事件类型白名单单元测试：只接受 Domain.Events 下的具体领域事件类型。</summary>
[TestFixture]
public class OutboxEventSerializerTests
{
    /// <summary>合法领域事件按 FullName + JSON 载荷完整往返。</summary>
    [Test]
    public void Deserialize_AllowedDomainEvent_RoundTrips()
    {
        var userId = Guid.NewGuid();
        var payload = OutboxEventSerializer.Serialize(new UserPasswordResetEvent(userId));

        var restored = OutboxEventSerializer.Deserialize(typeof(UserPasswordResetEvent).FullName!, payload);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.TypeOf<UserPasswordResetEvent>());
            Assert.That(((UserPasswordResetEvent)restored).UserId, Is.EqualTo(userId));
        });
    }

    /// <summary>T-16：Outbox 载荷与全应用共用一份 Options，写出 camelCase 且与 JsonHelper 完全一致。</summary>
    [Test]
    public void Serialize_UsesSharedCamelCaseOptions()
    {
        var domainEvent = new UserPasswordResetEvent(Guid.NewGuid());
        var payload = OutboxEventSerializer.Serialize(domainEvent);

        Assert.Multiple(() =>
        {
            Assert.That(JsonHelper.WebOptions.PropertyNamingPolicy, Is.EqualTo(JsonNamingPolicy.CamelCase));
            Assert.That(JsonHelper.WebOptions.PropertyNameCaseInsensitive, Is.True, "须兼容存量 PascalCase 载荷");
            Assert.That(payload, Does.Contain("\"eventName\""));
            Assert.That(payload, Does.Contain("\"occurredAt\""));
            Assert.That(payload, Does.Contain("\"userId\""));
            Assert.That(payload, Does.Not.Contain("\"UserId\""));
            Assert.That(payload, Is.EqualTo(JsonHelper.Serialize(domainEvent, domainEvent.GetType())));
        });
    }

    /// <summary>T-16：两套序列化入口往返一致（JsonHelper 写出 → Outbox 读回）。</summary>
    [Test]
    public void RoundTrip_JsonHelperToOutbox_PreservesPayload()
    {
        var userId = Guid.NewGuid();
        var domainEvent = new UserPasswordResetEvent(userId);
        var payload = JsonHelper.Serialize(domainEvent, typeof(UserPasswordResetEvent));

        var restored = OutboxEventSerializer.Deserialize(typeof(UserPasswordResetEvent).FullName!, payload);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.TypeOf<UserPasswordResetEvent>());
            Assert.That(((UserPasswordResetEvent)restored).UserId, Is.EqualTo(userId));
            Assert.That(restored.OccurredAt, Is.EqualTo(domainEvent.OccurredAt));
        });
    }

    /// <summary>T-16：存量 PascalCase 载荷在统一 Options 下仍可重放（升级不停机）。</summary>
    [Test]
    public void Deserialize_LegacyPascalCasePayload_StillRoundTrips()
    {
        var userId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow;
        var legacy = $"{{\"EventName\":\"UserPasswordResetEvent\",\"OccurredAt\":{JsonHelper.Serialize(occurredAt)},\"UserId\":{JsonHelper.Serialize(userId)}}}";

        var restored = OutboxEventSerializer.Deserialize(typeof(UserPasswordResetEvent).FullName!, legacy);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.TypeOf<UserPasswordResetEvent>());
            Assert.That(((UserPasswordResetEvent)restored).UserId, Is.EqualTo(userId));
            Assert.That(restored.OccurredAt, Is.EqualTo(occurredAt));
        });
    }

    /// <summary>T-16：携带集合 / 字典 / 枚举的事件载荷在统一 Options 下完整往返。</summary>
    [Test]
    public void RoundTrip_ComplexEventPayload_PreservesCollectionsAndEnum()
    {
        var userId = Guid.NewGuid();
        var user = new Domain.Entities.SysUser { Id = userId, UserName = "probe", Status = Domain.Shared.Enums.CommonStatus.Disabled };
        var domainEvent = new UserRolesUpdatedEvent(user, [Guid.NewGuid()], [Guid.NewGuid(), Guid.NewGuid()]);
        var payload = OutboxEventSerializer.Serialize(domainEvent);

        var restored = (UserRolesUpdatedEvent)OutboxEventSerializer.Deserialize(
            typeof(UserRolesUpdatedEvent).FullName!, payload);

        Assert.Multiple(() =>
        {
            Assert.That(restored.NewEntity.Id, Is.EqualTo(userId));
            Assert.That(restored.NewEntity.Status, Is.EqualTo(Domain.Shared.Enums.CommonStatus.Disabled));
            Assert.That(restored.BeforeRoleIds, Is.EqualTo(domainEvent.BeforeRoleIds));
            Assert.That(restored.AfterRoleIds, Is.EqualTo(domainEvent.AfterRoleIds));
            Assert.That(payload, Does.Not.Contain("PasswordHash"));
        });
    }

    /// <summary>任意 CLR 类型（非 DomainEvent 子类）不得作为反序列化目标。</summary>
    [Test]
    public void Deserialize_RejectsNonDomainEventClrType()
    {
        Assert.Throws<InvalidOperationException>(
            () => OutboxEventSerializer.Deserialize(typeof(string).FullName!, "\"text\""));
    }

    /// <summary>Domain.Events 命名空间之外的类型（即使名称形似事件）不得作为反序列化目标。</summary>
    [Test]
    public void Deserialize_RejectsTypeOutsideAllowedNamespace()
    {
        Assert.Throws<InvalidOperationException>(
            () => OutboxEventSerializer.Deserialize(typeof(DomainEventNotification).FullName!, "{}"));
    }

    /// <summary>抽象事件基类不得作为反序列化目标。</summary>
    [Test]
    public void Deserialize_RejectsAbstractDomainEvent()
    {
        Assert.Throws<InvalidOperationException>(
            () => OutboxEventSerializer.Deserialize(typeof(DomainEvent).FullName!, "{}"));
    }

    /// <summary>空白或超长类型名直接拒绝，不进入程序集扫描。</summary>
    [Test]
    public void Deserialize_RejectsBlankOrOversizedTypeName()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => OutboxEventSerializer.Deserialize(string.Empty, "{}"));
            Assert.Throws<InvalidOperationException>(() => OutboxEventSerializer.Deserialize(new string('A', 513), "{}"));
        });
    }
}
