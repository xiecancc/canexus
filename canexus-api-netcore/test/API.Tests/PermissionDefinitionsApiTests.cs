using System.Reflection;
using API;
using API.Authorization;
using Domain.Shared.Permissions;

namespace API.Tests;

/// <summary>
/// 权限定义器（P2-B4）集成断言：
/// 扫描 API 程序集全部 [RequirePermission] 声明，校验均已在集中注册表 PermissionDefinitions 中定义。
/// <para>与 Program.cs 启动期校验共用同一实现：本测试即"启动期一致性校验"的可执行证据。</para>
/// </summary>
[TestFixture]
public class PermissionDefinitionsApiTests
{
    /// <summary>验证控制器/Action 上所有权限声明都已在注册表中定义（新增权限必须进注册表，否则启动失败）。</summary>
    [Test]
    public void AllRequirePermissionAttributes_AreDeclaredInRegistry()
    {
        var undeclared = PermissionsStartupValidator.FindUndeclaredFromAssembly(typeof(Program).Assembly);

        Assert.That(undeclared, Is.Empty, $"存在未在 PermissionDefinitions 中声明的权限标识：{string.Join(", ", undeclared)}");
    }

    /// <summary>验证测试探针权限不进入生产权限注册表（探针控制器已移入测试程序集）。</summary>
    [Test]
    public void ProbePermission_IsNotInProductionRegistry()
    {
        Assert.That(PermissionDefinitions.All, Does.Not.Contain("test:protected:list"));
    }

    /// <summary>验证扫描实现能发现类级与 Action 级特性（至少发现全部控制器声明，数量>0）。</summary>
    [Test]
    public void Scanner_DiscoverAttributes()
    {
        var permissions = PermissionsStartupValidator.CollectPermissions(typeof(Program).Assembly);

        Assert.That(permissions, Is.Not.Empty, "扫描器应发现控制器上的权限声明");
        Assert.That(permissions, Does.Contain("system:user:list"), "应包含代表性权限 system:user:list");
    }
}
