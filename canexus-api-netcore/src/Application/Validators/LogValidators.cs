
using Application.Contracts.Commands;
using Application.Validators.Common;
using FluentValidation;

namespace Application.Validators;
/// <summary>删除登录日志命令验证器（乐观锁版本号入口兜底；日志为物理删除，版本号不参与比对但拒绝空令牌）。</summary>
public sealed class LoginLogDeleteCommandValidator() : DeleteCommandValidator<LoginLogDeleteCommand>;

/// <summary>删除操作日志命令验证器（乐观锁版本号入口兜底；日志为物理删除，版本号不参与比对但拒绝空令牌）。</summary>
public sealed class OperLogDeleteCommandValidator() : DeleteCommandValidator<OperLogDeleteCommand>;

/// <summary>
/// 危险清空命令的共享约束：显式确认 + 必填时间范围 + 范围上限 31 天 + 不得清空未来数据。
/// <para>命令经 FluentValidation 管道校验，缺任一项都返回 40001；仓储侧再以时间范围收敛 DELETE，
/// 两层同时防止「一次请求清空全表」。</para>
/// </summary>
/// <typeparam name="TCommand">清空命令类型。</typeparam>
public abstract class LogCleanCommandValidator<TCommand> : AbstractValidator<TCommand>
{
    /// <summary>单次清空允许的最大时间跨度（天）。</summary>
    public const int MaxRangeDays = 31;

    /// <summary>构造函数：装配确认标志与时间范围规则。</summary>
    /// <param name="beginSelector">起始日期选择器。</param>
    /// <param name="endSelector">结束日期选择器。</param>
    /// <param name="confirmSelector">确认标志选择器。</param>
    protected LogCleanCommandValidator(
        Func<TCommand, DateTime> beginSelector,
        Func<TCommand, DateTime> endSelector,
        Func<TCommand, bool> confirmSelector)
    {
        RuleFor(command => confirmSelector(command))
            .Must(value => value)
            .WithMessage("清空日志须显式确认（confirm=true）");
        RuleFor(command => beginSelector(command))
            .NotEmpty()
            .WithMessage("清空日志须指定起始日期");
        RuleFor(command => endSelector(command))
            .NotEmpty()
            .WithMessage("清空日志须指定结束日期");
        RuleFor(command => command)
            .Must(command => endSelector(command).Date >= beginSelector(command).Date)
            .WithMessage("结束日期不得早于起始日期");
        RuleFor(command => command)
            .Must(command => (endSelector(command).Date - beginSelector(command).Date).TotalDays <= MaxRangeDays)
            .WithMessage($"单次清空的时间范围不得超过 {MaxRangeDays} 天");
        RuleFor(command => command)
            .Must(command => endSelector(command).Date <= DateTime.UtcNow.Date)
            .WithMessage("结束日期不得晚于今天");
    }
}

/// <summary>按时间范围清空登录日志命令验证器（危险操作约束）。</summary>
public sealed class LoginLogCleanCommandValidator()
    : LogCleanCommandValidator<LoginLogCleanCommand>(
        command => command.BeginTime,
        command => command.EndTime,
        command => command.Confirm);

/// <summary>按时间范围清空操作日志命令验证器（危险操作约束）。</summary>
public sealed class OperLogCleanCommandValidator()
    : LogCleanCommandValidator<OperLogCleanCommand>(
        command => command.BeginTime,
        command => command.EndTime,
        command => command.Confirm);
