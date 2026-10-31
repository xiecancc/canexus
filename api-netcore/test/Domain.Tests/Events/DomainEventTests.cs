
using Domain.Entities;
using Domain.Events;

namespace Domain.Tests.Events;
/// <summary>用于测试的领域事件（无实体状态）。</summary>
public class TestDomainEvent : DomainEvent;

/// <summary>用于测试的聚合实体。</summary>
public class SnapshotTestAggregate : AggregateBase;

/// <summary>领域事件根基类单元测试（事件名称 / 发生时间）。</summary>
[TestFixture]
public class DomainEventTests
{
    [Test]
    public void EventName_UsesConcreteTypeName()
    {
        // 事件类型由事件类本身表达：事件名称取具体事件类型的短类名
        var domainEvent = new TestDomainEvent();

        Assert.That(domainEvent.EventName, Is.EqualTo(nameof(TestDomainEvent)));
    }

    [Test]
    public void OccurredAt_GeneratedAtConstruction()
    {
        var before = DateTime.Now;
        var domainEvent = new TestDomainEvent();
        var after = DateTime.Now;

        // 发生时间在构造窗口内自动生成
        Assert.That(domainEvent.OccurredAt, Is.InRange(before, after.AddSeconds(1)));
    }
}

/// <summary>聚合级事件类型基类单元测试（创建 / 修改 / 删除 / 恢复基类携带实体状态）。</summary>
[TestFixture]
public class AggregateEventBaseTests
{
    private static readonly Guid EntityId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static SnapshotTestAggregate CreateEntity() => new() { Id = EntityId };

    [Test]
    public void DomainCreatedEvent_CarriesCreatedAggregate()
    {
        var entity = CreateEntity();
        TestCreatedEvent domainEvent = new(entity);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventName, Is.EqualTo("TestCreatedEvent"));
            Assert.That(domainEvent.NewEntity, Is.SameAs(entity));
        });
    }

    [Test]
    public void DomainUpdatedEvent_CarriesBeforeAndAfterStates()
    {
        var before = CreateEntity();
        var after = CreateEntity();
        TestUpdatedEvent domainEvent = new(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventName, Is.EqualTo("TestUpdatedEvent"));
            Assert.That(domainEvent.OldEntity, Is.SameAs(before));
            // Domain 即变更后状态
            Assert.That(domainEvent.NewEntity, Is.SameAs(after));
        });
    }

    [Test]
    public void DomainDeletedEvent_CarriesDeletedAggregate()
    {
        var entity = CreateEntity();
        TestDeletedEvent domainEvent = new(entity);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventName, Is.EqualTo("TestDeletedEvent"));
            Assert.That(domainEvent.NewEntity, Is.SameAs(entity));
        });
    }

    [Test]
    public void DomainRestoredEvent_CarriesRestoredAggregate()
    {
        var entity = CreateEntity();
        TestRestoredEvent domainEvent = new(entity);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventName, Is.EqualTo("TestRestoredEvent"));
            Assert.That(domainEvent.NewEntity, Is.SameAs(entity));
        });
    }

    /// <summary>用于测试的创建事件。</summary>
    public sealed class TestCreatedEvent(SnapshotTestAggregate aggregate) : AggregateCreatedEvent<SnapshotTestAggregate>(aggregate);

    /// <summary>用于测试的修改事件。</summary>
    public sealed class TestUpdatedEvent(SnapshotTestAggregate before, SnapshotTestAggregate after)
        : AggregateUpdatedEvent<SnapshotTestAggregate>(before, after);

    /// <summary>用于测试的删除事件。</summary>
    public sealed class TestDeletedEvent(SnapshotTestAggregate aggregate) : AggregateDeletedEvent<SnapshotTestAggregate>(aggregate);

    /// <summary>用于测试的恢复事件。</summary>
    public sealed class TestRestoredEvent(SnapshotTestAggregate aggregate) : AggregateRestoredEvent<SnapshotTestAggregate>(aggregate);
}

/// <summary>实体快照方法单元测试。</summary>
[TestFixture]
public class EntitySnapshotTests
{
    private static readonly Guid EntityId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Test]
    public void Snapshot_CapturesIndependentState()
    {
        // 浅拷贝快照：原实体后续修改不影响快照（值类型 / 字符串属性独立）
        var user = new SysUser { Id = EntityId, UserName = "before", NickName = "旧昵称" };
        var snapshot = user.Snapshot<SysUser>();

        user.UserName = "after";
        user.NickName = "新昵称";

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.UserName, Is.EqualTo("before"));
            Assert.That(snapshot.NickName, Is.EqualTo("旧昵称"));
            Assert.That(snapshot.Id, Is.EqualTo(EntityId));
        });
    }
}

/// <summary>各领域具体事件单元测试（事件名称与状态携带）。</summary>
[TestFixture]
public class ConcreteDomainEventTests
{
    private static readonly Guid EntityId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Test]
    public void UserEvents_CarryStatesAndEventNames()
    {
        var created = new UserCreatedEvent(new SysUser { Id = EntityId, UserName = "user" });
        var before = new SysUser { Id = EntityId, UserName = "user", NickName = "旧" };
        var after = new SysUser { Id = EntityId, UserName = "user", NickName = "新" };

        Assert.Multiple(() =>
        {
            Assert.That(created.EventName, Is.EqualTo("UserCreatedEvent"));
            Assert.That(created.NewEntity.Id, Is.EqualTo(EntityId));
            Assert.That(new UserUpdatedEvent(before, after).EventName, Is.EqualTo("UserUpdatedEvent"));
            Assert.That(new UserPasswordResetEvent(EntityId).EventName, Is.EqualTo("UserPasswordResetEvent"));
            Assert.That(new UserDeletedEvent(before).EventName, Is.EqualTo("UserDeletedEvent"));
        });
    }

    [Test]
    public void UserRolesUpdatedEvent_CarriesUserAndRoleIdsBeforeAndAfter()
    {
        var user = new SysUser { Id = EntityId, UserName = "user" };
        List<Guid> oldRoleIds = [Guid.Parse("00000000-0000-0000-0000-000000000002")];
        List<Guid> newRoleIds = [Guid.Parse("00000000-0000-0000-0000-000000000003")];
        var domainEvent = new UserRolesUpdatedEvent(user, oldRoleIds, newRoleIds);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.NewEntity, Is.SameAs(user));
            Assert.That(domainEvent.BeforeRoleIds, Is.EqualTo(oldRoleIds));
            Assert.That(domainEvent.AfterRoleIds, Is.EqualTo(newRoleIds));
        });
    }

    [Test]
    public void UserPasswordResetEvent_CarriesUserIdWithoutState()
    {
        // 重置密码：仅携带用户 Id，不携带实体状态（密码哈希属敏感数据）
        var domainEvent = new UserPasswordResetEvent(EntityId);

        Assert.That(domainEvent.UserId, Is.EqualTo(EntityId));
    }

    [Test]
    public void DictDataUpdatedEvent_ExposesMigrationInfoViaBefore()
    {
        // 类型迁移：旧类型由 OldEntity.DictType 表达，无需独立迁移字段
        var before = new SysDictData { Id = EntityId, DictType = "old_type" };
        var after = new SysDictData { Id = EntityId, DictType = "new_type" };
        var domainEvent = new DictDataUpdatedEvent(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.OldEntity.DictType, Is.EqualTo("old_type"));
            Assert.That(domainEvent.NewEntity.DictType, Is.EqualTo("new_type"));
        });
    }

    [Test]
    public void ConfigUpdatedEvent_ExposesRenamedKeyViaBefore()
    {
        // 键名变更：旧键名由 OldEntity.ConfigKey 表达
        var before = new SysConfig { Id = EntityId, ConfigKey = "old_key" };
        var after = new SysConfig { Id = EntityId, ConfigKey = "new_key" };
        var domainEvent = new ConfigUpdatedEvent(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.OldEntity.ConfigKey, Is.EqualTo("old_key"));
            Assert.That(domainEvent.NewEntity.ConfigKey, Is.EqualTo("new_key"));
        });
    }

    [Test]
    public void OtherDomainEvents_UseTypedEventNames()
    {
        var role = new SysRole { Id = EntityId };

        Assert.Multiple(() =>
        {
            Assert.That(new RoleCreatedEvent(role).EventName, Is.EqualTo("RoleCreatedEvent"));
            Assert.That(new RoleUpdatedEvent(role, role).EventName, Is.EqualTo("RoleUpdatedEvent"));
            Assert.That(new RoleDeletedEvent(role).EventName, Is.EqualTo("RoleDeletedEvent"));
            Assert.That(new MenuCreatedEvent(new SysMenu { Id = EntityId }).EventName, Is.EqualTo("MenuCreatedEvent"));
            Assert.That(new DeptUpdatedEvent(new SysDept { Id = EntityId }, new SysDept { Id = EntityId }).EventName, Is.EqualTo("DeptUpdatedEvent"));
            Assert.That(new DictTypeDeletedEvent(new SysDictType { Id = EntityId }).EventName, Is.EqualTo("DictTypeDeletedEvent"));
            Assert.That(new DictDataCreatedEvent(new SysDictData { Id = EntityId }).EventName, Is.EqualTo("DictDataCreatedEvent"));
            Assert.That(new DictDataDeletedEvent(new SysDictData { Id = EntityId }).EventName, Is.EqualTo("DictDataDeletedEvent"));
            Assert.That(new ConfigCreatedEvent(new SysConfig { Id = EntityId }).EventName, Is.EqualTo("ConfigCreatedEvent"));
            Assert.That(new ConfigDeletedEvent(new SysConfig { Id = EntityId }).EventName, Is.EqualTo("ConfigDeletedEvent"));
            Assert.That(new NoticeCreatedEvent(new SysNotice { Id = EntityId }).EventName, Is.EqualTo("NoticeCreatedEvent"));
            Assert.That(new NoticeUpdatedEvent(new SysNotice { Id = EntityId }, new SysNotice { Id = EntityId }).EventName, Is.EqualTo("NoticeUpdatedEvent"));
            Assert.That(new TenantDeletedEvent(new SysTenant { Id = EntityId }).EventName, Is.EqualTo("TenantDeletedEvent"));
            Assert.That(new JobCreatedEvent(new SysJob { Id = EntityId }).EventName, Is.EqualTo("JobCreatedEvent"));
            Assert.That(new JobUpdatedEvent(new SysJob { Id = EntityId }, new SysJob { Id = EntityId }).EventName, Is.EqualTo("JobUpdatedEvent"));
        });
    }

    [Test]
    public void LogDeletedEvent_SingleDelete_CarriesLogId()
    {
        // 单条删除：携带被删除的日志 Id
        var domainEvent = new LogDeletedEvent(LogDeletedEvent.OperLogType, EntityId);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventName, Is.EqualTo("LogDeletedEvent"));
            Assert.That(domainEvent.LogType, Is.EqualTo("oper"));
            Assert.That(domainEvent.LogId, Is.EqualTo(EntityId));
        });
    }

    [Test]
    public void LogDeletedEvent_CleanAll_LogIdIsNull()
    {
        // 清空全部：无单条定位，日志 Id 为 null
        var domainEvent = new LogDeletedEvent(LogDeletedEvent.LoginLogType);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.LogType, Is.EqualTo("login"));
            Assert.That(domainEvent.LogId, Is.Null);
        });
    }
}
