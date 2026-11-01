
using Application.Contracts.Commands;
using Application.Contracts.Jobs;
using Application.Validators.Common;
using Domain.Services;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using FluentValidation;

namespace Application.Validators;
/// <summary>定时任务写入请求验证器：名称 / 分组非空且长度受列宽约束、状态取值合法、
/// Cron 表达式与任务类型经调度端口由底层调度器判定（错误码保持 CronInvalid / JobTypeInvalid；
/// 创建 / 更新共用，消除重复定义）。</summary>
/// <typeparam name="TRequest">定时任务写入请求类型。</typeparam>
public class JobRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : JobRequest
{
    /// <summary>调度管理端口（Cron / 任务类型校验经端口由底层调度器判定）。</summary>
    private IJobScheduleManager ScheduleManager { get; }

    /// <summary>构造函数。</summary>
    /// <param name="scheduleManager">调度管理端口。</param>
    public JobRequestValidator(IJobScheduleManager scheduleManager)
    {
        ScheduleManager = scheduleManager;

        RuleFor(request => request.JobName)
            .Must(name => !string.IsNullOrEmpty(name?.Trim()))
            .WithMessage("任务名称不能为空")
            .Must(name => name!.Trim().Length <= 50)
            .WithMessage("任务名称长度不能超过 50 个字符");

        RuleFor(request => request.JobGroup)
            .Must(group => !string.IsNullOrEmpty(group?.Trim()))
            .WithMessage("任务分组不能为空")
            .Must(group => group!.Trim().Length <= 50)
            .WithMessage("任务分组长度不能超过 50 个字符");

        RuleFor(request => request.Status).StatusRule("状态取值不合法：仅支持 1 运行 / 0 暂停");

        RuleFor(request => request)
            .Custom((request, context) =>
            {
                var cron = request.CronExpression?.Trim() ?? string.Empty;
                if (!ScheduleManager.IsValidCronExpression(cron))
                {
                    throw new BusinessException(ErrorCodes.CronInvalid, $"Cron 表达式不合法：{cron}");
                }

                var jobType = request.JobType?.Trim() ?? string.Empty;
                if (!ScheduleManager.IsValidJobType(jobType))
                {
                    throw new BusinessException(ErrorCodes.JobTypeInvalid, $"任务类型「{jobType}」不可解析或未实现调度任务接口");
                }
            });
    }
}

/// <summary>创建定时任务命令验证器（桥接请求验证器）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="scheduleManager">调度管理端口。</param>
public sealed class JobCreateCommandValidator(IJobScheduleManager scheduleManager) :
    WriteCommandValidator<JobCreateCommand, JobCreateRequest>(new JobRequestValidator<JobCreateRequest>(scheduleManager));

/// <summary>更新定时任务命令验证器（桥接请求验证器）。</summary>
/// <remarks>构造函数。</remarks>
/// <param name="scheduleManager">调度管理端口。</param>
public sealed class JobUpdateCommandValidator(IJobScheduleManager scheduleManager) :
    WriteCommandValidator<JobUpdateCommand, JobUpdateRequest>(new JobRequestValidator<JobUpdateRequest>(scheduleManager));

/// <summary>修改定时任务状态命令验证器：状态仅允许 1 运行 / 0 暂停（乐观锁版本比对由处理器完成）。</summary>
public sealed class JobUpdateStatusCommandValidator : AbstractValidator<JobUpdateStatusCommand>
{
    /// <summary>构造函数。</summary>
    public JobUpdateStatusCommandValidator()
    {
        RuleFor(command => command.Request.Status).StatusRule("状态取值不合法：仅支持 1 运行 / 0 暂停");
    }
}
