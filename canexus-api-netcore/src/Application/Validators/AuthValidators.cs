
using Application.Contracts.Commands;
using Application.Validators.Common;
using FluentValidation;

namespace Application.Validators;
/// <summary>登录命令验证器：用户名 / 密码 / 验证码 Id / 验证码内容非空（承接自请求 DTO DataAnnotations 迁移的规则，口径一致）。</summary>
public sealed class AuthLoginCommandValidator : AbstractValidator<AuthLoginCommand>
{
    /// <summary>构造函数。</summary>
    public AuthLoginCommandValidator()
    {
        RuleFor(command => command.Request.UserName).NotBlankRule("用户名不能为空");
        RuleFor(command => command.Request.Password).NotBlankRule("密码不能为空");
        RuleFor(command => command.Request.CaptchaId).NotBlankRule("验证码 Id 不能为空");
        RuleFor(command => command.Request.CaptchaCode).NotBlankRule("验证码不能为空");
    }
}

/// <summary>刷新令牌命令验证器：refreshToken 非空（令牌有效性与轮换由处理器判定）。</summary>
public sealed class AuthRefreshTokenCommandValidator : AbstractValidator<AuthRefreshTokenCommand>
{
    /// <summary>构造函数。</summary>
    public AuthRefreshTokenCommandValidator()
    {
        RuleFor(command => command.RefreshToken).NotBlankRule("刷新令牌不能为空");
    }
}
