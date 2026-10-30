namespace Application.Tests.Validators;

using Application.Contracts.Commands;
using Application.Contracts.Menus;
using Application.Validators;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using FluentValidation.TestHelper;
using NUnit.Framework;

/// <summary>创建菜单命令验证器单元测试（跨属性耦合规则，自 Domain.Tests/Rules/MenuRulesTests 迁入）。</summary>
[TestFixture]
public class MenuValidatorTests
{
    private static CreateMenuCommand BuildCommand(Action<MenuCreateRequest> configure)
    {
        var request = new MenuCreateRequest { MenuType = MenuType.Directory, MenuName = "测试菜单", Path = "/test" };
        configure(request);
        return new CreateMenuCommand(request);
    }

    [Test]
    public void Validate_DirectoryWithoutPath_ThrowsMenuRuleInvalid()
    {
        var command = BuildCommand(request => request.Path = " ");
        Assert.That(
            () => new CreateMenuCommandValidator().Validate(command),
            Throws.TypeOf<BusinessException>().With.Property("Code").EqualTo(ErrorCodes.MenuRuleInvalid));
    }

    [Test]
    public void Validate_ItemWithoutComponent_ThrowsMenuRuleInvalid()
    {
        var command = BuildCommand(request =>
        {
            request.MenuType = MenuType.Menu;
            request.Component = null;
        });
        Assert.That(
            () => new CreateMenuCommandValidator().Validate(command),
            Throws.TypeOf<BusinessException>().With.Property("Code").EqualTo(ErrorCodes.MenuRuleInvalid));
    }

    [Test]
    public void Validate_ButtonWithoutPerms_ThrowsMenuRuleInvalid()
    {
        var command = BuildCommand(request =>
        {
            request.MenuType = MenuType.Button;
            request.Path = string.Empty;
            request.Perms = null;
        });
        Assert.That(
            () => new CreateMenuCommandValidator().Validate(command),
            Throws.TypeOf<BusinessException>().With.Property("Code").EqualTo(ErrorCodes.MenuRuleInvalid));
    }

    [Test]
    public void Validate_ValidDirectory_Passes()
    {
        var command = BuildCommand(request => request.MenuType = MenuType.Directory);
        Assert.That(() => new CreateMenuCommandValidator().Validate(command), Throws.Nothing);
    }

    [Test]
    public void Validate_InvalidStatus_ThrowsMenuRuleInvalid()
    {
        var command = BuildCommand(request => request.Status = (CommonStatus)2);
        Assert.That(
            () => new CreateMenuCommandValidator().Validate(command),
            Throws.TypeOf<BusinessException>().With.Property("Code").EqualTo(ErrorCodes.MenuRuleInvalid));
    }
}
