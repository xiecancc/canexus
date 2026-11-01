namespace Domain.Shared.Enums;

/// <summary>
/// 执行结果状态：1 成功 / 0 失败（登录日志、操作日志与定时任务执行日志共用的结果语义）。
/// </summary>
public enum ExecutionStatus
{
    /// <summary>失败。</summary>
    Failure = 0,

    /// <summary>成功。</summary>
    Success = 1
}
