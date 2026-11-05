
using Domain.Entities;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;

namespace Domain.Tests.Entities;
/// <summary>聚合不变量守卫单元测试：内置数据保护规则内聚于聚合，拒绝操作时抛出带业务错误码的异常。</summary>
[TestFixture]
public class AggregateGuardTests
{
    /// <summary>验证 admin 用户名识别为内置超管账号，非 admin 不识别。</summary>
    [Test]
    public void IsBuiltInAdmin_AdminUserName_ReturnsTrue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new SysUser { UserName = "admin" }.IsBuiltInAdmin, Is.True);
            Assert.That(new SysUser { UserName = "Admin" }.IsBuiltInAdmin, Is.False, "账号比较区分大小写");
            Assert.That(new SysUser { UserName = "zhangsan" }.IsBuiltInAdmin, Is.False);
        });
    }

    /// <summary>验证内置超管账号调用守卫时抛出 UserAdminProtected 错误码与动作文案。</summary>
    [Test]
    public void EnsureNotBuiltInAdmin_AdminUser_ThrowsWithProtectedCode()
    {
        var user = new SysUser { UserName = "admin" };

        var ex = Assert.Throws<BusinessException>(() => user.EnsureNotBuiltInAdmin("删除"));

        Assert.Multiple(() =>
        {
            Assert.That(ex.Code, Is.EqualTo(ErrorCodes.UserAdminProtected));
            Assert.That(ex.Message, Does.Contain("删除"));
        });
    }

    /// <summary>验证普通用户通过守卫（不抛出异常）。</summary>
    [Test]
    public void EnsureNotBuiltInAdmin_NormalUser_Passes()
    {
        Assert.DoesNotThrow(() => new SysUser { UserName = "zhangsan" }.EnsureNotBuiltInAdmin("编辑"));
    }

    /// <summary>验证 super_admin 角色识别为内置超管角色，其他角色不识别。</summary>
    [Test]
    public void IsSuperAdmin_SuperAdminRoleKey_ReturnsTrue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new SysRole { RoleKey = "super_admin" }.IsSuperAdmin, Is.True);
            Assert.That(new SysRole { RoleKey = "common" }.IsSuperAdmin, Is.False);
        });
    }

    /// <summary>验证内置超管角色调用守卫时抛出 RoleProtected 错误码与动作文案。</summary>
    [Test]
    public void EnsureNotSuperAdmin_SuperAdminRole_ThrowsWithProtectedCode()
    {
        var role = new SysRole { RoleKey = "super_admin" };

        var ex = Assert.Throws<BusinessException>(() => role.EnsureNotSuperAdmin("启停"));

        Assert.Multiple(() =>
        {
            Assert.That(ex.Code, Is.EqualTo(ErrorCodes.RoleProtected));
            Assert.That(ex.Message, Does.Contain("启停"));
        });
    }

    /// <summary>验证普通角色通过守卫（不抛出异常）。</summary>
    [Test]
    public void EnsureNotSuperAdmin_NormalRole_Passes()
    {
        Assert.DoesNotThrow(() => new SysRole { RoleKey = "common" }.EnsureNotSuperAdmin("编辑"));
    }

    /// <summary>验证保留权限字符 super_admin 被静态守卫拦截（RoleKeyReserved）。</summary>
    [Test]
    public void EnsureRoleKeyNotReserved_SuperAdminKey_ThrowsWithReservedCode()
    {
        var ex = Assert.Throws<BusinessException>(() => SysRole.EnsureRoleKeyNotReserved("super_admin"));

        Assert.That(ex.Code, Is.EqualTo(ErrorCodes.RoleKeyReserved));
    }

    /// <summary>验证非保留权限字符通过静态守卫（不抛出异常）。</summary>
    [Test]
    public void EnsureRoleKeyNotReserved_NormalKey_Passes()
    {
        Assert.DoesNotThrow(() => SysRole.EnsureRoleKeyNotReserved("finance_admin"));
    }

    /// <summary>验证内置参数调用删除守卫时抛出 ConfigBuiltInProtected。</summary>
    [Test]
    public void EnsureDeletable_BuiltInConfig_ThrowsWithBuiltInCode()
    {
        var config = new SysConfig { BuiltIn = true };

        var ex = Assert.Throws<BusinessException>(() => config.EnsureDeletable());

        Assert.That(ex.Code, Is.EqualTo(ErrorCodes.ConfigBuiltInProtected));
    }

    /// <summary>验证非内置参数通过删除守卫（不抛出异常）。</summary>
    [Test]
    public void EnsureDeletable_NormalConfig_Passes()
    {
        Assert.DoesNotThrow(() => new SysConfig { BuiltIn = false }.EnsureDeletable());
    }
}
