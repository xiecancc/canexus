
using Application.Contracts.Common;
using Domain.Shared.Enums;
using Domain.Shared.Paging;

namespace Application.Contracts.Jobs;
/// <summary>定时任务分页查询请求（GET /api/system/job/page）。</summary>
public class JobPageRequest : AggregatePagedRequest
{
    /// <summary>关键字：模糊匹配任务名称。</summary>
    public string? Keyword { get; set; }

    /// <summary>状态：运行（1）/ 暂停（0）；为空不过滤。</summary>
    public CommonStatus? Status { get; set; }
}

/// <summary>定时任务列表行（分页数据项）。</summary>
public class JobListResponse : AggregatePagedResponse
{
    /// <summary>状态：运行（1），暂停（0）。</summary>
    public CommonStatus Status { get; set; }

    /// <summary>任务名称。</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>任务分组。</summary>
    public string JobGroup { get; set; } = string.Empty;

    /// <summary>Cron 表达式。</summary>
    public string CronExpression { get; set; } = string.Empty;

    /// <summary>任务类型（程序集限定类型名）。</summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>是否允许并发执行。</summary>
    public bool Concurrent { get; set; }

}

/// <summary>定时任务写入请求基类：创建 / 更新共同字段（镜像 SysJob 聚合的启停 / 备注与业务字段；平台级数据不做租户隔离）。</summary>
public abstract class JobRequest : AggregateChangeRequest
{
    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>备注。</summary>
    public string? Description { get; set; }

    /// <summary>任务名称。</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>任务分组。</summary>
    public string JobGroup { get; set; } = "DEFAULT";

    /// <summary>Cron 表达式（经调度端口校验）。</summary>
    public string CronExpression { get; set; } = string.Empty;

    /// <summary>任务类型（实现 Quartz.IJob 的程序集限定类型名）。</summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>是否允许并发执行。</summary>
    public bool Concurrent { get; set; }
}

/// <summary>创建定时任务请求（POST /api/system/job；运行状态任务创建后立即入调度）。</summary>
public class JobCreateRequest : JobRequest
{
}

/// <summary>更新定时任务请求（PUT /api/system/job/{id}；分组变更时旧调度一并清理；Version 须回传乐观锁版本号）。</summary>
public class JobUpdateRequest : JobRequest
{
}

/// <summary>修改任务状态请求（PUT /api/system/job/{id}/status；运行即入调度、暂停即移出；Version 须回传乐观锁版本号）。</summary>
public class JobStatusRequest : AggregateChangeRequest
{
    /// <summary>状态：运行（1），暂停（0）。</summary>
    public CommonStatus Status { get; set; } = CommonStatus.Enabled;
}

/// <summary>删除定时任务请求（DELETE /api/system/job/{id}；Version 须回传乐观锁版本号）。</summary>
public class JobDeleteRequest
{
    /// <summary>乐观锁版本号（须与聚合当前版本一致才允许删除）。</summary>
    public Guid Version { get; set; }
}

/// <summary>任务执行日志分页查询请求（GET /api/system/job-log/page）。</summary>
public class JobLogPageRequest : AggregatePagedRequest
{
    /// <summary>任务 Id 精确过滤。</summary>
    public Guid? JobId { get; set; }

    /// <summary>执行状态：成功（1）/ 失败（0）；为空不过滤。</summary>
    public ExecutionStatus? Status { get; set; }
}

/// <summary>任务执行日志列表行（分页数据项）。</summary>
public class JobLogListResponse : AggregatePagedResponse
{
    /// <summary>执行状态：成功（1），失败（0）。</summary>
    public ExecutionStatus Status { get; set; }

    /// <summary>任务 Id。</summary>
    public Guid JobId { get; set; }

    /// <summary>任务名称。</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>执行结果摘要。</summary>
    public string? Result { get; set; }

    /// <summary>异常信息（失败时）。</summary>
    public string? Exception { get; set; }

    /// <summary>执行耗时（毫秒）。</summary>
    public long CostMs { get; set; }

    /// <summary>触发时间。</summary>
    public DateTime TriggerTime { get; set; }
}
