namespace Domain.Tests.Events;

using Domain.Events;
using Domain.Shared.Enums;

/// <summary>用于测试的领域事件。</summary>
/// <param name="EventTypeValue">事件类型。</param>
/// <param name="AggregateIdValue">聚合根标识。</param>
/// <param name="PayloadValue">载荷。</param>
public class TestDomainEvent(string eventTypeValue, Guid? aggregateIdValue, IReadOnlyDictionary<string, object?>? payloadValue)
    : DomainEvent(eventTypeValue, aggregateIdValue, payloadValue)
{
}

/// <summary>领域事件基类通用模型单元测试（事件名称 / 事件类型 / 发生时间 / 聚合根标识 / 载荷）。</summary>
[TestFixture]
public class DomainEventTests
{
    private static readonly Guid AggregateId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Test]
    public void EventName_UsesConcreteTypeName()
    {
        // 事件名称取具体事件类型的短类名，供监听器路由与日志排查
        var domainEvent = new TestDomainEvent("test.changed", AggregateId, null);

        Assert.That(domainEvent.EventName, Is.EqualTo(nameof(TestDomainEvent)));
    }

    [Test]
    public void Fields_ExplicitlyProvided_ArePreserved()
    {
        var before = DateTime.Now;
        var payload = new Dictionary<string, object?> { ["changeType"] = "Updated" };
        var domainEvent = new TestDomainEvent("test.changed", AggregateId, payload);
        var after = DateTime.Now;

        Assert.Multiple(() =>
        {
            // 事件类型 / 聚合根标识 / 载荷按构造参数原样保留
            Assert.That(domainEvent.EventType, Is.EqualTo("test.changed"));
            Assert.That(domainEvent.AggregateId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.Payload, Is.EqualTo(payload));
            // 发生时间在构造窗口内自动生成
            Assert.That(domainEvent.OccurredAt, Is.InRange(before, after.AddSeconds(1)));
        });
    }

    [Test]
    public void Payload_NotProvided_DefaultsToEmpty()
    {
        // 缺省载荷为空只读字典（非 null），监听器可安全枚举
        var domainEvent = new TestDomainEvent("test.changed", AggregateId, null);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.Payload, Is.Not.Null);
            Assert.That(domainEvent.Payload, Is.Empty);
        });
    }

    [Test]
    public void AggregateId_DefaultsToNull_WhenNotProvided()
    {
        // 非聚合级事件聚合根标识为 null
        var domainEvent = new TestDomainEvent("test.changed", null, null);

        Assert.That(domainEvent.AggregateId, Is.Null);
    }

    [Test]
    public void UserChangedEvent_CarriesUserIdAndChangeType()
    {
        // 用户变更事件：聚合根标识即用户 Id，载荷携带 camelCase 键
        var domainEvent = new UserChangedEvent(AggregateId, AggregateChangeType.Updated);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventName, Is.EqualTo("UserChangedEvent"));
            Assert.That(domainEvent.EventType, Is.EqualTo("user.changed"));
            Assert.That(domainEvent.UserId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.ChangeType, Is.EqualTo(AggregateChangeType.Updated));
            Assert.That(domainEvent.AggregateId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.Payload["aggregateId"], Is.EqualTo(AggregateId));
            Assert.That(domainEvent.Payload["changeType"], Is.EqualTo("Updated"));
        });
    }

    [Test]
    public void RoleChangedEvent_CarriesRoleIdAndChangeType()
    {
        var domainEvent = new RoleChangedEvent(AggregateId, AggregateChangeType.Deleted);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("role.changed"));
            Assert.That(domainEvent.RoleId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.ChangeType, Is.EqualTo(AggregateChangeType.Deleted));
            Assert.That(domainEvent.Payload["aggregateId"], Is.EqualTo(AggregateId));
            Assert.That(domainEvent.Payload["changeType"], Is.EqualTo("Deleted"));
        });
    }

    [Test]
    public void MenuChangedEvent_CarriesMenuIdAndChangeType()
    {
        var domainEvent = new MenuChangedEvent(AggregateId, AggregateChangeType.Created);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("menu.changed"));
            Assert.That(domainEvent.MenuId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.ChangeType, Is.EqualTo(AggregateChangeType.Created));
            Assert.That(domainEvent.Payload["aggregateId"], Is.EqualTo(AggregateId));
            Assert.That(domainEvent.Payload["changeType"], Is.EqualTo("Created"));
        });
    }
}

/// <summary>新增领域事件单元测试（部门 / 字典 / 配置 / 通知 / 租户 / 作业 / 日志域）。</summary>
[TestFixture]
public class AdditionalDomainEventTests
{
    private static readonly Guid AggregateId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Test]
    public void DeptChangedEvent_CarriesDeptIdAndChangeType()
    {
        var domainEvent = new DeptChangedEvent(AggregateId, AggregateChangeType.Updated);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventName, Is.EqualTo("DeptChangedEvent"));
            Assert.That(domainEvent.EventType, Is.EqualTo("dept.changed"));
            Assert.That(domainEvent.DeptId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.ChangeType, Is.EqualTo(AggregateChangeType.Updated));
        });
    }

    [Test]
    public void DictTypeChangedEvent_CarriesTypeIdAndChangeType()
    {
        var domainEvent = new DictTypeChangedEvent(AggregateId, AggregateChangeType.Deleted);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("dict.type.changed"));
            Assert.That(domainEvent.DictTypeId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.ChangeType, Is.EqualTo(AggregateChangeType.Deleted));
        });
    }

    [Test]
    public void DictDataChangedEvent_CarriesTypeAndMigrationInfo()
    {
        // 类型迁移：载荷携带迁移前旧类型
        var domainEvent = new DictDataChangedEvent(AggregateId, "new_type", AggregateChangeType.Updated, "old_type");

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("dict.data.changed"));
            Assert.That(domainEvent.DictDataId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.DictType, Is.EqualTo("new_type"));
            Assert.That(domainEvent.OriginalDictType, Is.EqualTo("old_type"));
            Assert.That(domainEvent.Payload["dictType"], Is.EqualTo("new_type"));
            Assert.That(domainEvent.Payload["originalDictType"], Is.EqualTo("old_type"));
        });
    }

    [Test]
    public void DictDataChangedEvent_WithoutMigration_OriginalTypeIsNull()
    {
        var domainEvent = new DictDataChangedEvent(AggregateId, "some_type", AggregateChangeType.Created);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.OriginalDictType, Is.Null);
            Assert.That(domainEvent.Payload["originalDictType"], Is.Null);
        });
    }

    [Test]
    public void ConfigChangedEvent_CarriesKeyAndMigrationInfo()
    {
        // 键名变更：载荷携带变更前旧键名
        var domainEvent = new ConfigChangedEvent(AggregateId, "new_key", AggregateChangeType.Updated, "old_key");

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("config.changed"));
            Assert.That(domainEvent.ConfigId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.ConfigKey, Is.EqualTo("new_key"));
            Assert.That(domainEvent.OriginalConfigKey, Is.EqualTo("old_key"));
            Assert.That(domainEvent.Payload["configKey"], Is.EqualTo("new_key"));
            Assert.That(domainEvent.Payload["originalConfigKey"], Is.EqualTo("old_key"));
        });
    }

    [Test]
    public void NoticeChangedEvent_CarriesNoticeIdAndChangeType()
    {
        var domainEvent = new NoticeChangedEvent(AggregateId, AggregateChangeType.Created);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("notice.changed"));
            Assert.That(domainEvent.NoticeId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.ChangeType, Is.EqualTo(AggregateChangeType.Created));
        });
    }

    [Test]
    public void TenantChangedEvent_CarriesTenantIdAndChangeType()
    {
        var domainEvent = new TenantChangedEvent(AggregateId, AggregateChangeType.Updated);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("tenant.changed"));
            Assert.That(domainEvent.TenantId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.ChangeType, Is.EqualTo(AggregateChangeType.Updated));
        });
    }

    [Test]
    public void JobChangedEvent_CarriesJobIdAndChangeType()
    {
        var domainEvent = new JobChangedEvent(AggregateId, AggregateChangeType.Deleted);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("job.changed"));
            Assert.That(domainEvent.JobId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.ChangeType, Is.EqualTo(AggregateChangeType.Deleted));
        });
    }

    [Test]
    public void LogChangedEvent_SingleDelete_CarriesLogId()
    {
        // 单条删除：聚合根标识即日志 Id
        var domainEvent = new LogChangedEvent(LogChangedEvent.OperLogType, AggregateChangeType.Deleted, AggregateId);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.EventType, Is.EqualTo("log.changed"));
            Assert.That(domainEvent.LogType, Is.EqualTo("oper"));
            Assert.That(domainEvent.ChangeType, Is.EqualTo(AggregateChangeType.Deleted));
            Assert.That(domainEvent.LogId, Is.EqualTo(AggregateId));
            Assert.That(domainEvent.AggregateId, Is.EqualTo(AggregateId));
        });
    }

    [Test]
    public void LogChangedEvent_CleanAll_LogIdIsNull()
    {
        // 清空全部：无单条定位，聚合根标识为 null
        var domainEvent = new LogChangedEvent(LogChangedEvent.LoginLogType, AggregateChangeType.Deleted);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.LogType, Is.EqualTo("login"));
            Assert.That(domainEvent.LogId, Is.Null);
            Assert.That(domainEvent.AggregateId, Is.Null);
        });
    }

    [Test]
    public void UserChangedEvent_PasswordReset_CarriesChangeType()
    {
        // 重置密码：使用专用变更类型，为强制下线等监听器预留语义
        var domainEvent = new UserChangedEvent(AggregateId, AggregateChangeType.PasswordReset);

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.ChangeType, Is.EqualTo(AggregateChangeType.PasswordReset));
            Assert.That(domainEvent.Payload["changeType"], Is.EqualTo("PasswordReset"));
        });
    }

    [Test]
    public void AggregateChangedEvent_MergesStandardAndExtraPayload()
    {
        // 聚合级基类：标准载荷（aggregateId / changeType）与领域追加载荷合并
        var domainEvent = new DictDataChangedEvent(AggregateId, "some_type", AggregateChangeType.Updated, "old_type");

        Assert.Multiple(() =>
        {
            Assert.That(domainEvent.Payload["aggregateId"], Is.EqualTo(AggregateId));
            Assert.That(domainEvent.Payload["changeType"], Is.EqualTo("Updated"));
            Assert.That(domainEvent.Payload["dictType"], Is.EqualTo("some_type"));
            Assert.That(domainEvent.Payload["originalDictType"], Is.EqualTo("old_type"));
        });
    }
}
