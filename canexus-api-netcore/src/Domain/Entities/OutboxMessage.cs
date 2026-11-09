using Domain.Shared.Enums;
using SqlSugar;

namespace Domain.Entities;

/// <summary>
/// 领域事件 Outbox 消息实体（P1-A2）：业务事务内持久化的领域事件快照，
/// 事务提交后由投递服务/后台 worker 投递，投递失败保留记录并按次数重试。
/// <para>写入与业务命令同事务（命令失败则事件一并回滚，事件与业务状态强一致）；
/// 处理状态更新经独立连接（CopyNew）提交，不依赖原事务的存活。</para>
/// <para>基础设施消息实体（P1-R3）：不继承 AggregateBase / DomainBase——Outbox 记录是基础设施
/// 消息而非业务聚合，不参与乐观锁（Version）、软删除（DeletedAt，若过滤生效会静默吞掉待投递消息）
/// 与租户过滤（消息必须全局可见，投递方可能处于任意租户上下文），也不受审计 Aop 自动填充；
/// 主键与创建时间在 <see cref="Create"/> 工厂显式赋值。</para>
/// </summary>
[SugarTable("sys_outbox_message", TableDescription = "领域事件 Outbox 消息实体：业务事务内持久化的领域事件快照， 事务提交后由投递服务/后台 worker 投递，投递失败保留记录并按次数重试。")]
public sealed class OutboxMessage
{
    /// <summary>消息 Id（全局 GUID，工厂显式生成——非 DomainBase 体系，无 Aop 自动填充）。</summary>
    [SugarColumn(ColumnDescription = "消息 Id（全局 GUID，工厂显式生成——非 DomainBase 体系，无 Aop 自动填充）。", IsPrimaryKey = true)]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>消息创建时间（UTC，工厂显式赋值；同时是清理保留期的基准）。</summary>
    [SugarColumn(ColumnDescription = "消息创建时间（UTC，工厂显式赋值；同时是清理保留期的基准）。")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>事件类型（CLR 完整类型名，如 Domain.Events.UserPasswordResetEvent）。</summary>
    [SugarColumn(ColumnDescription = "事件类型（CLR 完整类型名，如 Domain.Events.UserPasswordResetEvent）。", Length = 255)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>事件序列化载荷（System.Text.Json，敏感字段已排除）。</summary>
    [SugarColumn(ColumnDescription = "事件序列化载荷（System.Text.Json，敏感字段已排除）。", ColumnDataType = "longtext")]
    public string Payload { get; set; } = string.Empty;

    /// <summary>处理状态（见 <see cref="OutboxMessageStatus"/>）。</summary>
    [SugarColumn(ColumnDescription = "处理状态（见 OutboxMessageStatus）。")]
    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;

    /// <summary>成功投递时间；null 表示尚未投递成功。</summary>
    [SugarColumn(ColumnDescription = "成功投递时间；null 表示尚未投递成功。", IsNullable = true)]
    public DateTime? ProcessedAt { get; set; }

    /// <summary>已尝试投递次数（首次插入为 0，每次失败 +1）。</summary>
    [SugarColumn(ColumnDescription = "已尝试投递次数（首次插入为 0，每次失败 +1）。")]
    public int AttemptCount { get; set; }

    /// <summary>最近一次投递失败原因。</summary>
    [SugarColumn(ColumnDescription = "最近一次投递失败原因。", IsNullable = true, Length = 1000)]
    public string? LastError { get; set; }

    /// <summary>认领者标识（副本实例：主机名 + 进程 Id + 启动随机串）；null 表示当前未被认领。</summary>
    [SugarColumn(ColumnDescription = "认领者标识（副本实例）；null 表示未被认领。", IsNullable = true, Length = 100)]
    public string? ClaimOwner { get; set; }

    /// <summary>认领时间（UTC）。</summary>
    [SugarColumn(ColumnDescription = "认领时间（UTC）。", IsNullable = true)]
    public DateTime? ClaimedAt { get; set; }

    /// <summary>租约截止时间（UTC）：到期后其他副本可重新认领（防止认领者崩溃导致消息卡死）。</summary>
    [SugarColumn(ColumnDescription = "租约截止时间（UTC）：到期后其他副本可重新认领。", IsNullable = true)]
    public DateTime? LeaseUntil { get; set; }

    /// <summary>下一次允许重试的时间（UTC，退避重试）；null / 已到期表示可立即认领。</summary>
    [SugarColumn(ColumnDescription = "下一次允许重试的时间（UTC，退避重试）；null 表示可立即认领。", IsNullable = true)]
    public DateTime? NextRetryAt { get; set; }

    /// <summary>创建待处理消息。</summary>
    /// <param name="eventType">事件 CLR 完整类型名。</param>
    /// <param name="payload">事件序列化载荷。</param>
    /// <returns>待处理状态的 Outbox 消息。</returns>
    public static OutboxMessage Create(string eventType, string payload)
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            EventType = eventType,
            Payload = payload,
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0
        };
    }
}
