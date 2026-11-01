
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
        return new ConfigCreateCommand(new ConfigCreateRequest
        {
            ConfigName = "测试参数",
            ConfigKey = configKey,
            ConfigValue = "1"
        });
    }

    [Test]
    public void Validate_BlankKey_HasFailure()
    {
        var result = new ConfigCreateCommandValidator().TestValidate(BuildCommand("  "));
        result.ShouldHaveValidationErrorFor(command => command.Request.ConfigKey);
    }

    [Test]
    public void Validate_TooLongKey_HasFailure()
    {
        var result = new ConfigCreateCommandValidator().TestValidate(BuildCommand(new string('k', 101)));
        result.ShouldHaveValidationErrorFor(command => command.Request.ConfigKey);
    }

    [Test]
    public void Validate_ValidKey_NoFailures()
    {
        var result = new ConfigCreateCommandValidator().TestValidate(BuildCommand("sys.user.initPassword"));
        result.ShouldNotHaveAnyValidationErrors();
    }
}
