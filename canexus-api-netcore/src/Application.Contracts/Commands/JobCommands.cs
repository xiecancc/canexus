
using Application.Contracts.Abstractions;
using Application.Contracts.Jobs;
using Domain.Shared.Permissions;

namespace Application.Contracts.Commands;
/// <summary>创建定时任务命令（Cron 与任务类型校验；status=1 时创建后立即入调度）。</summary>
[RequireCommandPermission(PermissionDefinitions.MonitorJobAdd)]
public sealed record JobCreateCommand : AggregateCreateCommand<JobInputRequest>
{
    /// <summary>构造创建定时任务命令。</summary>
    /// <param name="request">创建请求。</param>
    public JobCreateCommand(JobInputRequest request) : base(request) { }
}

/// <summary>删除定时任务命令（同时移出调度）。</summary>
/// <param name="Id">任务 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
[RequireCommandPermission(PermissionDefinitions.MonitorJobRemove)]
public sealed record JobDeleteCommand(Guid Id, Guid Version) : AggregateDeleteCommand(Id, Version);

/// <summary>恢复定时任务命令（解除软删除墓碑并同步调度器；幂等执行，不存在或未删除时静默忽略）。</summary>
/// <param name="Id">任务 Id。</param>
/// <param name="Version">乐观锁版本号（与删除相同的路由参数模式）。</param>
[RequireCommandPermission(PermissionDefinitions.MonitorJobRestore)]
public sealed record JobRestoreCommand(Guid Id, Guid Version) : AggregateRestoreCommand(Id, Version);

/// <summary>立即触发一次执行命令（不影响既定 Cron 调度）。</summary>
/// <param name="Id">任务 Id。</param>
[RequireCommandPermission(PermissionDefinitions.MonitorJobRun)]
public sealed record JobRunOnceCommand(Guid Id) : Request, ICommand;

/// <summary>更新定时任务命令（按新定义重调度；分组变更时同步清理旧调度键）。</summary>
[RequireCommandPermission(PermissionDefinitions.MonitorJobEdit)]
public sealed record JobUpdateCommand : AggregateUpdateCommand<JobInputRequest>
{
    /// <summary>构造更新定时任务命令。</summary>
    /// <param name="id">任务 Id。</param>
    /// <param name="version">乐观锁版本号。</param>
    /// <param name="request">更新请求。</param>
    public JobUpdateCommand(Guid id, Guid version, JobInputRequest request) : base(id, version, request) { }
}

/// <summary>修改定时任务状态命令（1 运行入调度，0 暂停移出调度）。</summary>
/// <param name="Id">任务 Id。</param>
/// <param name="Version">乐观锁版本号。</param>
/// <param name="Request">状态请求。</param>
[RequireCommandPermission(PermissionDefinitions.MonitorJobChangeStatus)]
public sealed record JobUpdateStatusCommand(Guid Id, Guid Version, JobStatusRequest Request) : AggregateUpdateCommand<JobStatusRequest>(Id, Version, Request);
