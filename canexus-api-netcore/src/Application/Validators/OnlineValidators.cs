using Application.Contracts.Commands;
using Application.Validators.Common;
using FluentValidation;

namespace Application.Validators;
/// <summary>强制下线在线会话命令验证器（会话记录 Id 必填兜底）。</summary>
public sealed class OnlineForceLogoutCommandValidator : AbstractValidator<OnlineForceLogoutCommand>
{
    /// <summary>构造函数。</summary>
    public OnlineForceLogoutCommandValidator()
    {
        RuleFor(command => command.TokenId).NoEmptyRule("会话 Id 不能为空");
    }
}
