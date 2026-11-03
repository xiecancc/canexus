namespace Domain.Events;

/// <summary>日志删除领域事件：登录 / 操作日志单条删除或物理清空成功后发布。</summary>
/// <para>特殊事件：日志为物理删除且按 Id 操作（不加载实体，无实体状态可携带），清空全部时日志 Id 为 null，
/// 故不适用泛型 / 聚合级事件基类，直接继承 <see cref="DomainEvent"/>。</para>
/// <param name="logType">日志类型：oper 操作日志 / login 登录日志。</param>
/// <param name="logId">被删除的日志 Id；清空全部时为 null。</param>
public sealed class LogDeletedEvent(string logType, Guid? logId = null) : DomainEvent
{
    /// <summary>操作日志类型标识。</summary>
    public const string OperLogType = "oper";

    /// <summary>登录日志类型标识。</summary>
    public const string LoginLogType = "login";

    /// <summary>日志类型：oper 操作日志 / login 登录日志。</summary>
    public string LogType { get; } = logType;

    /// <summary>被删除的日志 Id；清空全部时为 null。</summary>
    public Guid? LogId { get; } = logId;
}
