using API.Authorization;
using Application.Contracts.Abstractions;
using Domain.Shared.Permissions;

namespace API.Tests;
/// <summary>
/// 命令级权限声明覆盖测试（P0-5）：全部写命令必须显式声明权限策略，
/// 且 [RequireCommandPermission] 引用的权限标识必须全部在 PermissionDefinitions 注册表中声明。
/// </summary>
[TestFixture]
public class CommandPermissionCoverageTests
{
    /// <summary>扫描 Application.Contracts 中全部具体写命令，缺少权限策略的清单应为空。</summary>
    [Test]
    public void AllCommands_HavePermissionPolicy()
    {
        var missing = CommandPermissionStartupValidator.FindUnpermittedCommands(typeof(ICommand).Assembly);

        Assert.That(missing, Is.Empty, "以下写命令缺少权限声明：\n" + string.Join("\n", missing));
    }

    /// <summary>全部 [RequireCommandPermission] 权限标识均已在注册表声明。</summary>
    [Test]
    public void AllCommandPermissions_AreDeclared()
    {
        var permissions = CommandPermissionStartupValidator.CollectCommandPermissions(typeof(ICommand).Assembly);
        var undeclared = PermissionRegistryValidator.FindUndeclared(permissions);

        Assert.That(undeclared, Is.Empty, "以下命令权限未在注册表声明：\n" + string.Join("\n", undeclared));
    }
}
