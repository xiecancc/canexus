
using Application.Contracts.Commands;
using Application.Validators.Common;
using FluentValidation;

namespace Application.Validators;
/// <summary>刷新令牌命令验证器：refreshToken 非空（令牌有效性与轮换由处理器判定）。</summary>
public sealed class AuthRefreshTokenCommandValidator : AbstractValidator<AuthRefreshTokenCommand>
{
    /// <summary>构造函数。</summary>
    public AuthRefreshTokenCommandValidator()
    {
        RuleFor(command => command.RefreshToken).NotBlankRule("刷新令牌不能为空");
    }
}
