
using Application.Contracts.Abstractions;
using Application.Contracts.Jobs;

namespace Application.Contracts.Commands;
/// <summary>创建定时任务命令（Cron 与任务类型校验；status=1 时创建后立即入调度）。</summary>
/// <param name="Request">创建请求。</param>
public sealed record CreateJobCommand(JobCreateRequest Request) : ICommand<Guid>;

/// <summary>删除定时任务命令（同时移出调度）。</summary>
/// <param name="Id">任务 Id。</param>
public sealed record DeleteJobCommand(Guid Id) : ICommand;

/// <summary>立即触发一次执行命令（不影响既定 Cron 调度）。</summary>
/// <param name="Id">任务 Id。</param>
public sealed record RunJobOnceCommand(Guid Id) : ICommand;

/// <summary>更新定时任务命令（按新定义重调度；分组变更时同步清理旧调度键）。</summary>
/// <param name="Id">任务 Id。</param>
/// <param name="Request">更新请求。</param>
public sealed record UpdateJobCommand(Guid Id, JobUpdateRequest Request) : ICommand;

/// <summary>修改定时任务状态命令（1 运行入调度，0 暂停移出调度）。</summary>
/// <param name="Id">任务 Id。</param>
/// <param name="Request">状态请求。</param>
public sealed record UpdateJobStatusCommand(Guid Id, JobStatusRequest Request) : ICommand;
