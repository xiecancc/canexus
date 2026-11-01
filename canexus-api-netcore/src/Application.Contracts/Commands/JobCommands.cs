
using Application.Contracts.Abstractions;
using Application.Contracts.Jobs;

namespace Application.Contracts.Commands;
/// <summary>创建定时任务命令（Cron 与任务类型校验；status=1 时创建后立即入调度）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record JobCreateCommand : AggregateCreateCommand<JobCreateRequest>
{
    public JobCreateCommand(JobCreateRequest request) : base(request) { }
}

/// <summary>删除定时任务命令（同时移出调度）。</summary>
/// <param name="Id">任务 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
public sealed record JobDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>立即触发一次执行命令（不影响既定 Cron 调度）。</summary>
/// <param name="Id">任务 Id。</param>
public sealed record JobRunOnceCommand(Guid Id) : Command;

/// <summary>更新定时任务命令（按新定义重调度；分组变更时同步清理旧调度键）。</summary>
/// <param name="Id">任务 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record JobUpdateCommand : AggregateUpdateCommand<JobUpdateRequest>
{
    public JobUpdateCommand(Guid id, JobUpdateRequest request) : base(id, request) { }
}

/// <summary>修改定时任务状态命令（1 运行入调度，0 暂停移出调度）。</summary>
/// <param name="Id">任务 Id。</param>
/// <param name="Request">状态请求。</param>
public sealed record JobUpdateStatusCommand(Guid Id, JobStatusRequest Request) : AggregateUpdateCommand<JobStatusRequest>(Id, Request);
