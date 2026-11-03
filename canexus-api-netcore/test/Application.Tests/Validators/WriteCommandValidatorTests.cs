
using Application.Contracts.Commands;
using Application.Contracts.Configs;
using Application.Validators;
using FluentValidation.TestHelper;
using NUnit.Framework;

namespace Application.Tests.Validators;
/// <summary>写命令验证器基类单元测试：乐观锁版本号的入口兜底（更新与删除均拒绝空令牌）。</summary>
[TestFixture]
public class WriteCommandValidatorTests
{
    private static ConfigInputRequest BuildRequest()
    {
        return new ConfigInputRequest
        {
            ConfigName = "测试参数",
            ConfigKey = "test.key",
            ConfigValue = "value"
        };
    }

    /// <summary>验证更新命令的版本号为空时校验产生失败。</summary>
    [Test]
    public void Update_EmptyVersion_HasFailure()
    {
        var result = new ConfigUpdateCommandValidator()
            .TestValidate(new ConfigUpdateCommand(Guid.NewGuid(), Guid.Empty, BuildRequest()));
        result.ShouldHaveValidationErrorFor(command => command.Version);
    }

    /// <summary>验证更新命令的版本号非空时校验不产生任何失败。</summary>
    [Test]
    public void Update_NonEmptyVersion_NoFailures()
    {
        var result = new ConfigUpdateCommandValidator()
            .TestValidate(new ConfigUpdateCommand(Guid.NewGuid(), Guid.NewGuid(), BuildRequest()));
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>验证删除命令的版本号为空时校验产生失败。</summary>
    [Test]
    public void Delete_EmptyVersion_HasFailure()
    {
        var result = new ConfigDeleteCommandValidator()
            .TestValidate(new ConfigDeleteCommand(Guid.NewGuid(), Guid.Empty));
        result.ShouldHaveValidationErrorFor(command => command.Version);
    }
}
