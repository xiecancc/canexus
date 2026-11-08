
using Application.Contracts.Commands;
using Application.Validators;
using FluentValidation.TestHelper;
using Infrastructure.Shared.Options;
using Microsoft.Extensions.Options;

namespace Application.Tests.Validators;

/// <summary>
/// 口令强度策略可配置回归：默认策略必须与历史规则（6-20 位、含字母与数字）完全一致，
/// 自定义策略须同时改变长度上下限与字符类要求，且失败提示随策略变化。
/// </summary>
[TestFixture]
public class PasswordPolicyTests
{
    /// <summary>构造带指定策略的改密命令验证器。</summary>
    /// <param name="policy">口令策略。</param>
    /// <returns>验证器实例。</returns>
    private static AuthChangePasswordCommandValidator CreateValidator(PasswordPolicyOption policy)
        => new(new StubOptionsMonitor<PasswordPolicyOption>(policy));

    /// <summary>构造改密命令。</summary>
    /// <param name="newPassword">新密码。</param>
    /// <returns>命令实例。</returns>
    private static AuthChangePasswordCommand Command(string newPassword)
        => new("old-pass-123", newPassword);

    /// <summary>默认策略：合规口令通过，且失败提示与历史文案一致。</summary>
    [Test]
    public void DefaultPolicy_KeepsHistoricalBehaviour()
    {
        var validator = CreateValidator(new PasswordPolicyOption());

        validator.TestValidate(Command("abc123")).ShouldNotHaveValidationErrorFor(command => command.NewPassword);

        var weak = validator.TestValidate(Command("abcdef"));
        weak.ShouldHaveValidationErrorFor(command => command.NewPassword)
            .WithErrorMessage("密码为 6-20 位，且需同时包含字母和数字");
    }

    /// <summary>默认策略：长度越界与空白字符仍按历史正则口径拒绝。</summary>
    [Test]
    public void DefaultPolicy_RejectsShortPasswordAndWhitespace()
    {
        var validator = CreateValidator(new PasswordPolicyOption());

        validator.TestValidate(Command("ab1")).ShouldHaveValidationErrorFor(command => command.NewPassword);
        validator.TestValidate(Command("abc 123")).ShouldHaveValidationErrorFor(command => command.NewPassword);
    }

    /// <summary>自定义策略：长度下限 12、强制大写与特殊字符时，规则与提示均随之变化。</summary>
    [Test]
    public void CustomPolicy_AppliesLengthAndCharacterClasses()
    {
        var policy = new PasswordPolicyOption
        {
            MinLength = 12,
            MaxLength = 32,
            RequireLetter = true,
            RequireDigit = true,
            RequireUppercase = true,
            RequireSpecialCharacter = true
        };
        var validator = CreateValidator(policy);

        validator.TestValidate(Command("abcdefghijk1")).ShouldHaveValidationErrorFor(command => command.NewPassword);
        validator.TestValidate(Command("Abcdefghijk1!")).ShouldNotHaveValidationErrorFor(command => command.NewPassword);

        var result = validator.TestValidate(Command("short1!"));
        result.ShouldHaveValidationErrorFor(command => command.NewPassword)
            .WithErrorMessage("密码为 12-32 位，且需同时包含字母、数字、大写字母、特殊字符");
    }

    /// <summary>配置自校验：最大长度不得小于最小长度。</summary>
    [Test]
    public void Option_RejectsInvertedLengthRange()
    {
        var policy = new PasswordPolicyOption { MinLength = 20, MaxLength = 6 };
        Assert.Throws<ArgumentException>(() => policy.Validate());
    }

    /// <summary>测试用 IOptionsMonitor 桩。</summary>
    /// <typeparam name="T">选项类型。</typeparam>
    /// <param name="value">固定返回的选项值。</param>
    private sealed class StubOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        /// <inheritdoc />
        public T CurrentValue => value;

        /// <inheritdoc />
        public T Get(string? name) => value;

        /// <inheritdoc />
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
