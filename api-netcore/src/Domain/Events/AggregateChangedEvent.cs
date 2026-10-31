namespace Domain.Events;

using Domain.Shared.Enums;

/// <summary>
/// 聚合级领域事件基类：统一「聚合根变更」事件的公共样板，消除各事件类的重复定义。
/// <para>标准载荷统一为 aggregateId + changeType（camelCase），派生事件经 extraPayload 追加
/// 领域特有数据；变更类型由本基类持有，派生类只需声明强类型访问器与事件类型字符串。</para>
/// <para>不适用场景：聚合根标识可空（如日志清空 LogChangedEvent）或非聚合级事件，
/// 直接继承 <see cref="DomainEvent"/> 自行组装载荷。</para>
/// </summary>
public abstract class AggregateChangedEvent : DomainEvent
{
    /// <summary>构造函数：由派生事件传入事件类型、聚合根标识、变更类型与追加载荷。</summary>
    /// <param name="eventType">事件类型（业务级命名，如 user.changed）。</param>
    /// <param name="aggregateId">聚合根标识。</param>
    /// <param name="changeType">变更类型。</param>
    /// <param name="extraPayload">领域特有追加载荷（键为 camelCase 命名）；缺省无追加。</param>
    protected AggregateChangedEvent(string eventType, Guid aggregateId, AggregateChangeType changeType, IReadOnlyDictionary<string, object?>? extraPayload = null)
        : base(eventType, aggregateId, MergePayload(aggregateId, changeType, extraPayload))
    {
        ChangeType = changeType;
    }

    /// <summary>变更类型。</summary>
    public AggregateChangeType ChangeType { get; }

    /// <summary>合并标准载荷（aggregateId / changeType）与领域追加载荷。</summary>
    private static IReadOnlyDictionary<string, object?> MergePayload(Guid aggregateId, AggregateChangeType changeType, IReadOnlyDictionary<string, object?>? extraPayload)
    {
        var payload = new Dictionary<string, object?>
        {
            ["aggregateId"] = aggregateId,
            ["changeType"] = changeType.ToString()
        };
        if (extraPayload is not null)
        {
            foreach (var pair in extraPayload)
            {
                payload[pair.Key] = pair.Value;
            }
        }

        return payload;
    }
}
