namespace Domain.Tests.Events;

using Domain.Entities;
using Domain.Events;

/// <summary>用于测试的领域事件。</summary>
/// <param name="EventTypeValue">事件类型。</param>
/// <param name="AggregateIdValue">聚合根标识。</param>
public class TestDomainEvent(string eventTypeValue, Guid? aggregateIdValue) : DomainEvent(eventTypeValue, aggregateIdValue);

/// <summary>用于测试的聚合根实体。</summary>
public class SnapshotTestAggregate : AggregateBase;

/// <summary>领域事件基类通用模型单元测试（事件名称 / 事件类型 / 发生时间 / 聚合根标识）。</summary>
[TestFixture]
public class DomainEventTests
{
    private static readonly Guid AggregateId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Test]
    public void EventName_UsesConcreteTypeName()
    {
        // 事件名称取具体事件类型的短类名，供监听器路由与日志排查
        var domainEvent = new TestDomainEvent("test.updated", AggregateId);

        Assert.That(domainEvent.EventName, Is.EqualTo(nameof(TestDomainEvent)));
    }

    [Test]
    public void Fields_ExplicitlyProvided_ArePreserved()
    {
        var before = DateTime.Now;
        var domainEvent = new TestDomainEvent("test.updated", AggregateId);
        var after = DateTime.Now;

        Assert.Multiple(() =>
        {
            // 事件类型 / 聚合根标识按构造参数原样保留，发生时间在构造窗口内自动生成
            Assert.That(domainEvent.EventType, Is.EqualTo("test.updated"));
            Assert.That(domainEvent.AggregateId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.OccurredAt, Is.InRange(before, after.AddSeconds(1)));
        });
    }

    [Test]
    public void AggregateId_DefaultsToNull_WhenNotProvided()
    {
        // 非聚合级事件聚合根标识为 null
        var domainEvent = new TestDomainEvent("test.updated", null);

        Assert.That(domainEvent.AggregateId, Is.Null);
    }

    [Test]
    public void Snapshot_CapturesIndependentState()
    {
        // 浅拷贝快照：原实体后续修改不影响快照（值类型 / 字符串属性独立）
        var user = new SysUser { Id = AggregateId, UserName = "before", NickName = "旧昵称" };
        var snapshot = user.Snapshot<SysUser>();

        user.UserName = "after";
        user.NickName = "新昵称";

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.UserName, Is.EqualTo("before"));
            Assert.That(snapshot.NickName, Is.EqualTo("旧昵称"));
            Assert.That(snapshot.Id, Is.EqualTo(AggregateId));
        });
    }
}

/// <summary>聚合级事件类型基类单元测试（创建 / 修改 / 删除 / 恢复基类携带状态与聚合根标识）。</summary>
[TestFixture]
public class AggregateEventBaseTests
{
    private static readonly Guid EntityId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static SnapshotTestAggregate CreateEntity() => new() { Id = EntityId };

    [Test]
    public void CreatedEvent_CarriesCreatedEntity()
    {
        var entity = CreateEntity();
        TestCreatedEvent domainEvent = new(entity);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("test.created"));
            Assert.That(domainEvent.Entity, Is.SameAs(entity));
            Assert.That(domainEvent.AggregateId, Is.EqualTo(EntityId));
        });
    }

    [Test]
    public void UpdatedEvent_CarriesBeforeAndAfterStates()
    {
        var before = CreateEntity();
        var after = CreateEntity();
        TestUpdatedEvent domainEvent = new(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("test.updated"));
            Assert.That(domainEvent.Before, Is.SameAs(before));
            Assert.That(domainEvent.After, Is.SameAs(after));
            // 聚合根标识取变更后状态
            Assert.That(domainEvent.AggregateId, Is.EqualTo(EntityId));
        });
    }

    [Test]
    public void DeletedEvent_CarriesDeletedEntity()
    {
        var entity = CreateEntity();
        TestDeletedEvent domainEvent = new(entity);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("test.deleted"));
            Assert.That(domainEvent.Entity, Is.SameAs(entity));
            Assert.That(domainEvent.AggregateId, Is.EqualTo(EntityId));
        });
    }

    [Test]
    public void RestoredEvent_CarriesRestoredEntity()
    {
        var entity = CreateEntity();
        TestRestoredEvent domainEvent = new(entity);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("test.restored"));
            Assert.That(domainEvent.Entity, Is.SameAs(entity));
            Assert.That(domainEvent.AggregateId, Is.EqualTo(EntityId));
        });
    }

    /// <summary>用于测试的创建事件。</summary>
    public sealed class TestCreatedEvent(SnapshotTestAggregate entity) : CreatedEvent<SnapshotTestAggregate>("test.created", entity);

    /// <summary>用于测试的修改事件。</summary>
    public sealed class TestUpdatedEvent(SnapshotTestAggregate before, SnapshotTestAggregate after)
        : UpdatedEvent<SnapshotTestAggregate>("test.updated", before, after);

    /// <summary>用于测试的删除事件。</summary>
    public sealed class TestDeletedEvent(SnapshotTestAggregate entity) : DeletedEvent<SnapshotTestAggregate>("test.deleted", entity);

    /// <summary>用于测试的恢复事件。</summary>
    public sealed class TestRestoredEvent(SnapshotTestAggregate entity) : RestoredEvent<SnapshotTestAggregate>("test.restored", entity);
}

/// <summary>各领域具体事件单元测试（事件类型命名与状态携带）。</summary>
[TestFixture]
public class ConcreteDomainEventTests
{
    private static readonly Guid EntityId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Test]
    public void UserEvents_CarryStatesAndEventType()
    {
        var created = new UserCreatedEvent(new SysUser { Id = EntityId, UserName = "user" });
        var before = new SysUser { Id = EntityId, UserName = "user", NickName = "旧" };
        var after = new SysUser { Id = EntityId, UserName = "user", NickName = "新" };

        Assert.Multiple(() =>
        {
            Assert.That(created.EventType, Is.EqualTo("user.created"));
            Assert.That(created.AggregateId, Is.EqualTo(EntityId));
            Assert.That(new UserUpdatedEvent(before, after).EventType, Is.EqualTo("user.updated"));
            Assert.That(new UserRolesUpdatedEvent(EntityId, [EntityId], []).EventType, Is.EqualTo("user.roles_updated"));
            Assert.That(new UserPasswordResetEvent(EntityId).EventType, Is.EqualTo("user.password_reset"));
            Assert.That(new UserDeletedEvent(before).EventType, Is.EqualTo("user.deleted"));
        });
    }

    [Test]
    public void UserRolesUpdatedEvent_CarriesRoleIdsBeforeAndAfter()
    {
        var oldRoleIds = new List<Guid> { Guid.Parse("00000000-0000-0000-0000-000000000002") };
        var newRoleIds = new List<Guid> { Guid.Parse("00000000-0000-0000-0000-000000000003") };
        var domainEvent = new UserRolesUpdatedEvent(EntityId, oldRoleIds, newRoleIds);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.UserId, Is.EqualTo(EntityId));
            Assert.That(domainEvent.BeforeRoleIds, Is.EqualTo(oldRoleIds));
            Assert.That(domainEvent.AfterRoleIds, Is.EqualTo(newRoleIds));
        });
    }

    [Test]
    public void UserPasswordResetEvent_CarriesUserIdWithoutState()
    {
        // 重置密码：仅携带用户 Id，不携带聚合状态（密码哈希属敏感数据）
        var domainEvent = new UserPasswordResetEvent(EntityId);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.UserId, Is.EqualTo(EntityId));
            Assert.That(domainEvent.EventType, Is.EqualTo("user.password_reset"));
        });
    }

    [Test]
    public void DictDataUpdatedEvent_ExposesMigrationInfoViaBefore()
    {
        // 类型迁移：旧类型由 Before.DictType 表达，无需独立迁移字段
        var before = new SysDictData { Id = EntityId, DictType = "old_type" };
        var after = new SysDictData { Id = EntityId, DictType = "new_type" };
        var domainEvent = new DictDataUpdatedEvent(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("dict.data.updated"));
            Assert.That(domainEvent.Before.DictType, Is.EqualTo("old_type"));
            Assert.That(domainEvent.After.DictType, Is.EqualTo("new_type"));
        });
    }

    [Test]
    public void ConfigUpdatedEvent_ExposesRenamedKeyViaBefore()
    {
        // 键名变更：旧键名由 Before.ConfigKey 表达
        var before = new SysConfig { Id = EntityId, ConfigKey = "old_key" };
        var after = new SysConfig { Id = EntityId, ConfigKey = "new_key" };
        var domainEvent = new ConfigUpdatedEvent(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("config.updated"));
            Assert.That(domainEvent.Before.ConfigKey, Is.EqualTo("old_key"));
            Assert.That(domainEvent.After.ConfigKey, Is.EqualTo("new_key"));
        });
    }

    [Test]
    public void OtherDomainEvents_UseTypedEventTypeNames()
    {
        var entity = new SysRole { Id = EntityId };

        Assert.Multiple(() =>
        {
            Assert.That(new RoleCreatedEvent(entity).EventType, Is.EqualTo("role.created"));
            Assert.That(new RoleUpdatedEvent(entity, entity).EventType, Is.EqualTo("role.updated"));
            Assert.That(new RoleDeletedEvent(entity).EventType, Is.EqualTo("role.deleted"));
            Assert.That(new MenuCreatedEvent(new SysMenu { Id = EntityId }).EventType, Is.EqualTo("menu.created"));
            Assert.That(new DeptUpdatedEvent(new SysDept { Id = EntityId }, new SysDept { Id = EntityId }).EventType, Is.EqualTo("dept.updated"));
            Assert.That(new DictTypeDeletedEvent(new SysDictType { Id = EntityId }).EventType, Is.EqualTo("dict.type.deleted"));
            Assert.That(new DictDataCreatedEvent(new SysDictData { Id = EntityId }).EventType, Is.EqualTo("dict.data.created"));
            Assert.That(new DictDataDeletedEvent(new SysDictData { Id = EntityId }).EventType, Is.EqualTo("dict.data.deleted"));
            Assert.That(new ConfigCreatedEvent(new SysConfig { Id = EntityId }).EventType, Is.EqualTo("config.created"));
            Assert.That(new ConfigDeletedEvent(new SysConfig { Id = EntityId }).EventType, Is.EqualTo("config.deleted"));
            Assert.That(new NoticeCreatedEvent(new SysNotice { Id = EntityId }).EventType, Is.EqualTo("notice.created"));
            Assert.That(new NoticeUpdatedEvent(new SysNotice { Id = EntityId }, new SysNotice { Id = EntityId }).EventType, Is.EqualTo("notice.updated"));
            Assert.That(new TenantDeletedEvent(new SysTenant { Id = EntityId }).EventType, Is.EqualTo("tenant.deleted"));
            Assert.That(new JobCreatedEvent(new SysJob { Id = EntityId }).EventType, Is.EqualTo("job.created"));
            Assert.That(new JobUpdatedEvent(new SysJob { Id = EntityId }, new SysJob { Id = EntityId }).EventType, Is.EqualTo("job.updated"));
        });
    }

    [Test]
    public void LogDeletedEvent_SingleDelete_CarriesLogId()
    {
        // 单条删除：聚合根标识即日志 Id
        var domainEvent = new LogDeletedEvent(LogDeletedEvent.OperLogType, EntityId);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("log.deleted"));
            Assert.That(domainEvent.LogType, Is.EqualTo("oper"));
            Assert.That(domainEvent.LogId, Is.EqualTo(EntityId));
            Assert.That(domainEvent.AggregateId, Is.EqualTo(EntityId));
        });
    }

    [Test]
    public void LogDeletedEvent_CleanAll_LogIdIsNull()
    {
        // 清空全部：无单条定位，聚合根标识为 null
        var domainEvent = new LogDeletedEvent(LogDeletedEvent.LoginLogType);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.LogType, Is.EqualTo("login"));
            Assert.That(domainEvent.LogId, Is.Null);
            Assert.That(domainEvent.AggregateId, Is.Null);
        });
    }
}
