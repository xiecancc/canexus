
using Application.Contracts.Commands;
using Application.Contracts.Configs;
using Application.Validators;
using FluentValidation.TestHelper;
using NUnit.Framework;

namespace Application.Tests.Validators;
/// <summary>创建参数配置命令验证器单元测试（键名格式，自 Domain.Tests/Rules/ConfigRulesTests 迁入）。</summary>
[TestFixture]
public class ConfigValidatorTests
{
    private static ConfigCreateCommand BuildCommand(string configKey)
    {
        return new ConfigCreateCommand(new ConfigInputRequest
        {
            ConfigName = "测试参数",
            ConfigKey = configKey,
            ConfigValue = "1"
        });
    }

    /// <summary>验证参数键名为空白时校验产生失败。</summary>
    [Test]
    public void Validate_BlankKey_HasFailure()
    {
        var result = new ConfigCreateCommandValidator().TestValidate(BuildCommand("  "));
        result.ShouldHaveValidationErrorFor(command => command.Request.ConfigKey);
    }

    /// <summary>验证参数键名超长（超过 100 字符）时校验产生失败。</summary>
    [Test]
    public void Validate_TooLongKey_HasFailure()
    {
        var result = new ConfigCreateCommandValidator().TestValidate(BuildCommand(new string('k', 101)));
        result.ShouldHaveValidationErrorFor(command => command.Request.ConfigKey);
    }

    /// <summary>验证参数键名合法时校验不产生任何失败。</summary>
    [Test]
    public void Validate_ValidKey_NoFailures()
    {
        var result = new ConfigCreateCommandValidator().TestValidate(BuildCommand("sys.user.initPassword"));
        result.ShouldNotHaveAnyValidationErrors();
    }
}
