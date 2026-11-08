
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

/// <summary>自助修改密码命令验证器：原密码非空、新密码满足强度规则（原密码正确性与用户存在性由处理器校验）。</summary>
public sealed class AuthChangePasswordCommandValidator : AbstractValidator<AuthChangePasswordCommand>
{
    /// <summary>构造函数。</summary>
    public AuthChangePasswordCommandValidator()
    {
        RuleFor(command => command.OldPassword).NotBlankRule("原密码不能为空");
        RuleFor(command => command.NewPassword).PasswordRule();
    }
}

/// <summary>登出命令验证器（写命令入口覆盖：刷新令牌可空，但不得为纯空白）。</summary>
/// <remarks>构造函数。</remarks>
public sealed class AuthLogoutCommandValidator : AbstractValidator<AuthLogoutCommand>
{
    /// <summary>构造函数：装配刷新令牌规则。</summary>
    public AuthLogoutCommandValidator()
    {
        RuleFor(command => command.RefreshToken)
            .Must(token => token is null || !string.IsNullOrWhiteSpace(token))
            .WithMessage("刷新令牌不得为空白字符串");
    }
}
