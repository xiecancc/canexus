
using Domain.Entities;
using Domain.Shared.Enums;

namespace Domain.Tests.Entities;
/// <summary>由序号生成测试用确定性 Guid（与种子数据占位主键同格式）。</summary>
public static class TreeTestIds
{
    /// <summary>按序号生成第 n 个测试用确定性 Guid。</summary>
    public static Guid Id(long n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");
}

/// <summary>菜单树 / 部门树装配规则单元测试（纯内存逻辑，针对 <see cref="AggregateTreeBase{TAggregate}.BuildTree"/>）。</summary>
[TestFixture]
public class TreeAssemblyTests
{
    /// <summary>全 0 Guid（根节点父 Id）。</summary>
    private static Guid Root => Guid.Empty;

    /// <summary>验证装配树时子节点正确嵌套到父节点下，且同级按 OrderNum 升序排列。</summary>
    [Test]
    public void BuildTree_NestsChildrenAndSortsByOrderNum()
    {
        List<SysMenu> menus =
        [
            new() { Id = TreeTestIds.Id(100), ParentId = Root, MenuType = MenuType.Directory, MenuName = "系统管理", Path = "system", OrderNum = 2 },
            new() { Id = TreeTestIds.Id(1), ParentId = Root, MenuType = MenuType.Menu, MenuName = "首页", Path = "/dashboard", Component = "dashboard/index", OrderNum = 1 },
            new() { Id = TreeTestIds.Id(102), ParentId = TreeTestIds.Id(100), MenuType = MenuType.Menu, MenuName = "角色管理", Path = "role", Component = "system/role/index", OrderNum = 2 },
            new() { Id = TreeTestIds.Id(101), ParentId = TreeTestIds.Id(100), MenuType = MenuType.Menu, MenuName = "用户管理", Path = "user", Component = "system/user/index", OrderNum = 1 },
            new() { Id = TreeTestIds.Id(1001), ParentId = TreeTestIds.Id(101), MenuType = MenuType.Button, MenuName = "用户查询", Path = "#", Perms = "system:user:query", OrderNum = 1 }
        ];

        var tree = SysMenu.BuildTree(menus);

        Assert.Multiple(() =>
        {
            Assert.That(tree, Has.Count.EqualTo(2), "根级应只有首页与系统管理");
            Assert.That(tree[0].MenuName, Is.EqualTo("首页"), "OrderNum 小者在前");
            Assert.That(tree[1].MenuName, Is.EqualTo("系统管理"));

            var system = tree[1];
            Assert.That(system.Children.Select(c => c.MenuName).ToList(),
                Is.EqualTo(["用户管理", "角色管理"]), "同级按 OrderNum 升序");

            var user = system.Children[0];
            Assert.That(user.Children.Single().Perms, Is.EqualTo("system:user:query"), "按钮应挂到菜单下");
        });
    }

    /// <summary>验证父节点缺失的孤儿节点会挂到根级而非被丢弃。</summary>
    [Test]
    public void BuildTree_OrphanNodeWithMissingParent_AttachesToRoot()
    {
        var missingParentId = Guid.NewGuid();
        List<SysMenu> menus =
        [
            new() { Id = TreeTestIds.Id(300), ParentId = missingParentId, MenuType = MenuType.Menu, MenuName = "孤儿菜单", Path = "orphan", Component = "orphan/index", OrderNum = 1 },
            new() { Id = TreeTestIds.Id(1), ParentId = Root, MenuType = MenuType.Menu, MenuName = "首页", Path = "/dashboard", Component = "dashboard/index", OrderNum = 2 }
        ];

        var tree = SysMenu.BuildTree(menus);

        Assert.Multiple(() =>
        {
            Assert.That(tree, Has.Count.EqualTo(2), "父级缺失的节点应挂到根级而非丢失");
            Assert.That(tree.Select(n => n.MenuName), Does.Contain("孤儿菜单"));
        });
    }

    /// <summary>验证部门树装配时同级节点按 OrderNum 升序排列。</summary>
    [Test]
    public void BuildTree_DeptSiblings_SortedByOrderNum()
    {
        List<SysDept> depts =
        [
            new() { Id = TreeTestIds.Id(100), ParentId = Root, DeptName = "总公司", OrderNum = 1 },
            new() { Id = TreeTestIds.Id(102), ParentId = TreeTestIds.Id(100), DeptName = "市场部", OrderNum = 2 },
            new() { Id = TreeTestIds.Id(101), ParentId = TreeTestIds.Id(100), DeptName = "研发部", OrderNum = 1 }
        ];

        var tree = SysDept.BuildTree(depts);

        Assert.Multiple(() =>
        {
            Assert.That(tree, Has.Count.EqualTo(1));
            Assert.That(tree[0].DeptName, Is.EqualTo("总公司"));
            Assert.That(tree[0].Children.Select(c => c.DeptName).ToList(),
                Is.EqualTo(["研发部", "市场部"]), "同级按 OrderNum 升序");
            Assert.That(tree[0].Children[0].Children, Is.Empty);
        });
    }

    /// <summary>验证 OrderNum 相同时按 Id 升序作为排序兜底。</summary>
    [Test]
    public void BuildTree_SameOrderNum_FallsBackToIdAscending()
    {
        List<SysDept> depts =
        [
            new() { Id = TreeTestIds.Id(202), ParentId = Root, DeptName = "后建部门", OrderNum = 1 },
            new() { Id = TreeTestIds.Id(201), ParentId = Root, DeptName = "先建部门", OrderNum = 1 }
        ];

        var tree = SysDept.BuildTree(depts);

        Assert.That(tree.Select(d => d.DeptName).ToList(),
            Is.EqualTo(["先建部门", "后建部门"]), "OrderNum 相同时按 Id 升序兜底");
    }
}
