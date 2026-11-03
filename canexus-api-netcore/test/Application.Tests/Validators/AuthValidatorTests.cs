
using Application.Contracts.Auth;
using Application.Contracts.Commands;
using Application.Validators;
using FluentValidation.TestHelper;
using NUnit.Framework;

namespace Application.Tests.Validators;
/// <summary>认证命令验证器单元测试（登录四字段非空与刷新令牌非空，承接自请求 DTO DataAnnotations 迁移的规则）。</summary>
[TestFixture]
public class AuthValidatorTests
{
    private static AuthLoginCommand BuildLoginCommand(string userName, string password, string captchaId, string captchaCode)
    {
        return new AuthLoginCommand(
            new LoginRequest
            {
                UserName = userName,
                Password = password,
                CaptchaId = captchaId,
                CaptchaCode = captchaCode
            }, Ip: null, UserAgent: null);
    }

    /// <summary>验证用户名为空白时登录命令校验产生失败。</summary>
    [Test]
    public void Validate_BlankUserName_HasFailure()
    {
        var result = new AuthLoginCommandValidator().TestValidate(BuildLoginCommand(" ", "pass123", "cap-1", "ab12"));
        result.ShouldHaveValidationErrorFor(command => command.Request.UserName);
    }

    /// <summary>验证密码为空白时登录命令校验产生失败。</summary>
    [Test]
    public void Validate_BlankPassword_HasFailure()
    {
        var result = new AuthLoginCommandValidator().TestValidate(BuildLoginCommand("admin", "", "cap-1", "ab12"));
        result.ShouldHaveValidationErrorFor(command => command.Request.Password);
    }

    /// <summary>验证验证码 ID 为空白时登录命令校验产生失败。</summary>
    [Test]
    public void Validate_BlankCaptchaId_HasFailure()
    {
        var result = new AuthLoginCommandValidator().TestValidate(BuildLoginCommand("admin", "pass123", " ", "ab12"));
        result.ShouldHaveValidationErrorFor(command => command.Request.CaptchaId);
    }

    /// <summary>验证验证码为空白时登录命令校验产生失败。</summary>
    [Test]
    public void Validate_BlankCaptchaCode_HasFailure()
    {
        var result = new AuthLoginCommandValidator().TestValidate(BuildLoginCommand("admin", "pass123", "cap-1", " "));
        result.ShouldHaveValidationErrorFor(command => command.Request.CaptchaCode);
    }

    /// <summary>验证登录四字段完整时校验不产生任何失败。</summary>
    [Test]
    public void Validate_CompleteLogin_NoFailures()
    {
        var result = new AuthLoginCommandValidator().TestValidate(BuildLoginCommand("admin", "pass123", "cap-1", "ab12"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>验证刷新令牌为空白时刷新命令校验产生失败。</summary>
    [Test]
    public void Validate_BlankRefreshToken_HasFailure()
    {
        var result = new AuthRefreshTokenCommandValidator().TestValidate(new AuthRefreshTokenCommand(" "));
        result.ShouldHaveValidationErrorFor(command => command.RefreshToken);
    }

    /// <summary>验证刷新令牌有效时校验不产生任何失败。</summary>
    [Test]
    public void Validate_ValidRefreshToken_NoFailures()
    {
        var result = new AuthRefreshTokenCommandValidator().TestValidate(new AuthRefreshTokenCommand("token-1"));
        result.ShouldNotHaveAnyValidationErrors();
    }
}
