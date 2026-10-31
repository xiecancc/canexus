
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
    private static CreateDictDataCommand BuildCommand(TagType tagType, CommonStatus status)
    {
        return new CreateDictDataCommand(new DictDataCreateRequest
        {
            DictType = "test_dict",
            DictLabel = "测试",
            DictValue = "1",
            TagType = tagType,
            Status = status
        });
    }

    [Test]
    public void Validate_InvalidTagType_HasFailure()
    {
        var result = new CreateDictDataCommandValidator().TestValidate(BuildCommand((TagType)9, CommonStatus.Enabled));
        result.ShouldHaveValidationErrorFor(command => command.Request.TagType);
    }

    [Test]
    public void Validate_InvalidStatus_HasFailure()
    {
        var result = new CreateDictDataCommandValidator().TestValidate(BuildCommand(TagType.Primary, (CommonStatus)2));
        result.ShouldHaveValidationErrorFor(command => command.Request.Status);
    }

    [Test]
    public void Validate_ValidFields_NoFailures()
    {
        var result = new CreateDictDataCommandValidator().TestValidate(BuildCommand(TagType.Info, CommonStatus.Disabled));
        result.ShouldNotHaveAnyValidationErrors();
    }
}
