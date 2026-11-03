
using Application.Contracts.Commands;
using Application.Contracts.Menus;
using Application.Validators;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using FluentValidation.TestHelper;
using NUnit.Framework;

namespace Application.Tests.Validators;
/// <summary>创建菜单命令验证器单元测试（跨属性耦合规则，自 Domain.Tests/Rules/MenuRulesTests 迁入）。</summary>
[TestFixture]
public class MenuValidatorTests
{
    private static MenuCreateCommand BuildCommand(Action<MenuInputRequest> configure)
    {
        var request = new MenuInputRequest { MenuType = MenuType.Directory, MenuName = "测试菜单", Path = "/test" };
        configure(request);
        return new MenuCreateCommand(request);
    }

    /// <summary>验证目录类型菜单缺少路由路径时抛出菜单规则异常。</summary>
    [Test]
    public void Validate_DirectoryWithoutPath_ThrowsMenuRuleInvalid()
    {
        var command = BuildCommand(request => request.Path = " ");
        Assert.That(
            () => new MenuCreateCommandValidator().Validate(command),
            Throws.TypeOf<BusinessException>().With.Property("Code").EqualTo(ErrorCodes.MenuRuleInvalid));
    }

    /// <summary>验证菜单类型缺少组件路径时抛出菜单规则异常。</summary>
    [Test]
    public void Validate_ItemWithoutComponent_ThrowsMenuRuleInvalid()
    {
        var command = BuildCommand(request =>
        {
            request.MenuType = MenuType.Menu;
            request.Component = null;
        });
        Assert.That(
            () => new MenuCreateCommandValidator().Validate(command),
            Throws.TypeOf<BusinessException>().With.Property("Code").EqualTo(ErrorCodes.MenuRuleInvalid));
    }

    /// <summary>验证按钮类型缺少权限标识时抛出菜单规则异常。</summary>
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
            () => new MenuCreateCommandValidator().Validate(command),
            Throws.TypeOf<BusinessException>().With.Property("Code").EqualTo(ErrorCodes.MenuRuleInvalid));
    }

    /// <summary>验证合法的目录类型菜单可以通过校验。</summary>
    [Test]
    public void Validate_ValidDirectory_Passes()
    {
        var command = BuildCommand(request => request.MenuType = MenuType.Directory);
        Assert.That(() => new MenuCreateCommandValidator().Validate(command), Throws.Nothing);
    }

    /// <summary>验证状态取值非法时抛出菜单规则异常。</summary>
    [Test]
    public void Validate_InvalidStatus_ThrowsMenuRuleInvalid()
    {
        var command = BuildCommand(request => request.Status = (CommonStatus)2);
        Assert.That(
            () => new MenuCreateCommandValidator().Validate(command),
            Throws.TypeOf<BusinessException>().With.Property("Code").EqualTo(ErrorCodes.MenuRuleInvalid));
    }
}
