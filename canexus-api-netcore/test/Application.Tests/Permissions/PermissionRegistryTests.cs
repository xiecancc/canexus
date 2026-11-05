using Domain.Shared.Permissions;

namespace Application.Tests.Permissions;

/// <summary>
/// 权限定义器（P2-B4）单元测试：
/// 验证集中式权限注册表（PermissionDefinitions + PermissionRegistryValidator）能够发现未声明权限并拒绝启动。
/// </summary>
[TestFixture]
public class PermissionRegistryTests
{
    /// <summary>验证 FindUndeclared 能识别未在注册表中声明的权限标识。</summary>
    [Test]
    public void FindUndeclared_ReturnsUnknownPermissions()
    {
        var undeclared = PermissionRegistryValidator.FindUndeclared(["system:user:list", "bogus:perm"]);

        Assert.That(undeclared, Is.EqualTo(new[] { "bogus:perm" }));
    }

    /// <summary>验证 EnsureDeclared 在存在未声明权限时抛出包含权限名的异常。</summary>
    [Test]
    public void EnsureDeclared_ThrowsWhenAnyUndeclared()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PermissionRegistryValidator.EnsureDeclared(["system:user:list", "bogus:perm"]));

        Assert.That(exception!.Message, Does.Contain("bogus:perm"));
    }

    /// <summary>验证注册表非空且全部条目符合"模块:动作:资源"三段式命名约定。</summary>
    [Test]
    public void Registry_AllEntriesFollowModuleActionResourceNaming()
    {
        Assert.That(PermissionDefinitions.All, Is.Not.Empty, "权限注册表不应为空");
        Assert.That(PermissionDefinitions.All, Is.All.Matches<string>(permission =>
            permission.Split(':').Length >= 3), "权限标识应遵循 模块:动作:资源 命名约定");
    }

    /// <summary>验证注册表内部一致性：All 集合与各常量声明保持一致（无重复、无遗漏）。</summary>
    [Test]
    public void Registry_AllCollectionMatchesDeclaredConstants()
    {
        var declaredConstants = typeof(PermissionDefinitions)
            .GetFields()
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string?)field.GetRawConstantValue())
            .Where(value => value is not null)
            .Cast<string>()
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var all = PermissionDefinitions.All.OrderBy(value => value, StringComparer.Ordinal).ToArray();

        Assert.That(all, Is.EqualTo(declaredConstants), "PermissionDefinitions.All 应与全部权限常量一一对应");
    }
}
