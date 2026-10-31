
using Application.Contracts.Commands;
using Application.Validators.Common;
using Domain.Services;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using FluentValidation;

namespace Application.Validators;
/// <summary>创建定时任务命令验证器：名称 / 分组非空且长度受列宽约束、状态取值合法、
/// Cron 表达式与任务类型经调度端口由底层调度器判定（错误码保持 CronInvalid / JobTypeInvalid）。</summary>
public sealed class CreateJobCommandValidator : AbstractValidator<CreateJobCommand>
{
    /// <summary>构造函数。</summary>
    /// <param name="scheduleManager">调度管理端口（Cron / 任务类型校验经端口由底层调度器判定）。</param>
    public CreateJobCommandValidator(IJobScheduleManager scheduleManager)
    {
        RuleFor(command => command.Request.JobName)
            .Must(name => !string.IsNullOrEmpty(name?.Trim()))
            .WithMessage("任务名称不能为空")
            .Must(name => name!.Trim().Length <= 50)
            .WithMessage("任务名称长度不能超过 50 个字符");

        RuleFor(command => command.Request.JobGroup)
            .Must(group => !string.IsNullOrEmpty(group?.Trim()))
            .WithMessage("任务分组不能为空")
            .Must(group => group!.Trim().Length <= 50)
            .WithMessage("任务分组长度不能超过 50 个字符");

        RuleFor(command => command.Request.Status).StatusRule("状态取值不合法：仅支持 1 运行 / 0 暂停");

        RuleFor(command => command.Request)
            .Custom((request, context) =>
            {
                var cron = request.CronExpression?.Trim() ?? string.Empty;
                if (!scheduleManager.IsValidCronExpression(cron))
                {
                    throw new BusinessException(ErrorCodes.CronInvalid, $"Cron 表达式不合法：{cron}");
                }

                var jobType = request.JobType?.Trim() ?? string.Empty;
                if (!scheduleManager.IsValidJobType(jobType))
                {
                    throw new BusinessException(ErrorCodes.JobTypeInvalid, $"任务类型「{jobType}」不可解析或未实现调度任务接口");
                }
            });
    }
}

/// <summary>更新定时任务命令验证器：同创建。</summary>
public sealed class UpdateJobCommandValidator : AbstractValidator<UpdateJobCommand>
{
    /// <summary>构造函数。</summary>
    /// <param name="scheduleManager">调度管理端口。</param>
    public UpdateJobCommandValidator(IJobScheduleManager scheduleManager)
    {
        RuleFor(command => command.Request.JobName)
            .Must(name => !string.IsNullOrEmpty(name?.Trim()))
            .WithMessage("任务名称不能为空")
            .Must(name => name!.Trim().Length <= 50)
            .WithMessage("任务名称长度不能超过 50 个字符");

        RuleFor(command => command.Request.JobGroup)
            .Must(group => !string.IsNullOrEmpty(group?.Trim()))
            .WithMessage("任务分组不能为空")
            .Must(group => group!.Trim().Length <= 50)
            .WithMessage("任务分组长度不能超过 50 个字符");

        RuleFor(command => command.Request.Status).StatusRule("状态取值不合法：仅支持 1 运行 / 0 暂停");

        RuleFor(command => command.Request)
            .Custom((request, context) =>
            {
                var cron = request.CronExpression?.Trim() ?? string.Empty;
                if (!scheduleManager.IsValidCronExpression(cron))
                {
                    throw new BusinessException(ErrorCodes.CronInvalid, $"Cron 表达式不合法：{cron}");
                }

                var jobType = request.JobType?.Trim() ?? string.Empty;
                if (!scheduleManager.IsValidJobType(jobType))
                {
                    throw new BusinessException(ErrorCodes.JobTypeInvalid, $"任务类型「{jobType}」不可解析或未实现调度任务接口");
                }
            });
    }
}
