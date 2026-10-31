namespace Domain.Events;

using Domain.Shared.Enums;

/// <summary>日志变更领域事件：登录 / 操作日志删除单条或物理清空成功后发布。清空时聚合根标识为 null（无单条定位）。</summary>
/// <param name="LogType">日志类型：oper 操作日志 / login 登录日志。</param>
/// <param name="ChangeType">变更类型（当前仅 Deleted）。</param>
/// <param name="LogId">被删除的日志 Id；清空全部时为 null。</param>
public sealed class LogChangedEvent(string logType, AggregateChangeType changeType, Guid? logId = null)
    : DomainEvent("log.changed", logId, new Dictionary<string, object?>
    {
        ["logType"] = logType,
        ["changeType"] = changeType.ToString(),
        ["logId"] = logId
    })
{
    /// <summary>操作日志类型标识。</summary>
    public const string OperLogType = "oper";

    /// <summary>登录日志类型标识。</summary>
    public const string LoginLogType = "login";

    /// <summary>日志类型：oper 操作日志 / login 登录日志。</summary>
    public string LogType { get; } = logType;

    /// <summary>变更类型（当前仅 Deleted）。</summary>
    public AggregateChangeType ChangeType { get; } = changeType;

    /// <summary>被删除的日志 Id；清空全部时为 null。</summary>
    public Guid? LogId { get; } = logId;
}
