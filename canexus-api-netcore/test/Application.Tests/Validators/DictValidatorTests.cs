
using Application.Contracts.Commands;
using Application.Contracts.Dicts;
using Application.Validators;
using Domain.Shared.Enums;
using FluentValidation.TestHelper;
using NUnit.Framework;

namespace Application.Tests.Validators;
/// <summary>创建字典数据命令验证器单元测试（标签样式 / 状态取值，自 Domain.Tests/Rules/DictRulesTests 迁入）。</summary>
[TestFixture]
public class DictValidatorTests
{
    private static DictDataCreateCommand BuildCommand(TagType tagType, CommonStatus status)
    {
        return new DictDataCreateCommand(new DictDataInputRequest
        {
            DictType = "test_dict",
            DictLabel = "测试",
            DictValue = "1",
            TagType = tagType,
            Status = status
        });
    }

    /// <summary>验证标签样式取值非法时校验产生失败。</summary>
    [Test]
    public void Validate_InvalidTagType_HasFailure()
    {
        var result = new DictDataCreateCommandValidator().TestValidate(BuildCommand((TagType)9, CommonStatus.Enabled));
        result.ShouldHaveValidationErrorFor(command => command.Request.TagType);
    }

    /// <summary>验证状态取值非法时校验产生失败。</summary>
    [Test]
    public void Validate_InvalidStatus_HasFailure()
    {
        var result = new DictDataCreateCommandValidator().TestValidate(BuildCommand(TagType.Primary, (CommonStatus)2));
        result.ShouldHaveValidationErrorFor(command => command.Request.Status);
    }

    /// <summary>验证标签样式与状态均合法时校验不产生任何失败。</summary>
    [Test]
    public void Validate_ValidFields_NoFailures()
    {
        var result = new DictDataCreateCommandValidator().TestValidate(BuildCommand(TagType.Info, CommonStatus.Disabled));
        result.ShouldNotHaveAnyValidationErrors();
    }
}
