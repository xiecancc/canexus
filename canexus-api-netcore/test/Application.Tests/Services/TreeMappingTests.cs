
using Application.Contracts.Depts;
using Application.Contracts.Menus;
using Application.Mappings;
using AutoMapper;
using Domain.Entities;
using Domain.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Application.Tests.Services;
/// <summary>实体树（BuildTree 装配）→ 契约 Response 的递归映射单元测试（端到端覆盖查询处理器的建树管线）。</summary>
[TestFixture]
public class TreeMappingTests
{
    /// <summary>由序号生成测试用确定性 Guid。</summary>
    private static Guid Id(long n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    /// <summary>实体 → 响应模型映射器（CommonProfile 基类链 + SysMenuProfile / SysDeptProfile，与应用同一 AddAutoMapper 构建方式）。</summary>
    private readonly IMapper _mapper = CreateMapper();

    /// <summary>构建 AutoMapper 映射器。</summary>
    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutoMapper(expression =>
        {
            expression.AddProfile<CommonProfile>();
            expression.AddProfile<SysMenuProfile>();
            expression.AddProfile<SysDeptProfile>();
        });
        return services.BuildServiceProvider().GetRequiredService<IMapper>();
    }

    /// <summary>验证菜单实体树递归映射为菜单树响应模型。</summary>
    [Test]
    public void MenuEntityTree_MapsRecursivelyToMenuTreeVO()
    {
        List<SysMenu> menus =
        [
            new() { Id = Id(100), ParentId = Guid.Empty, MenuType = MenuType.Directory, MenuName = "系统管理", Path = "system", OrderNum = 2 },
            new() { Id = Id(101), ParentId = Id(100), MenuType = MenuType.Menu, MenuName = "用户管理", Path = "user", Component = "system/user/index", OrderNum = 1 },
            new() { Id = Id(1001), ParentId = Id(101), MenuType = MenuType.Button, MenuName = "用户查询", Path = "#", Perms = "system:user:query", OrderNum = 1 }
        ];

        var tree = _mapper.Map<List<MenuTreeResponse>>(SysMenu.BuildTree(menus));

        Assert.Multiple(() =>
        {
            Assert.That(tree, Has.Count.EqualTo(1));
            var system = tree.Single();
            Assert.That(system.MenuName, Is.EqualTo("系统管理"));
            Assert.That(system.OrderNum, Is.EqualTo(2));

            var user = system.Children.Single();
            Assert.That(user.MenuName, Is.EqualTo("用户管理"), "子级应随 AutoMapper 递归映射");
            Assert.That(user.Children.Single().Perms, Is.EqualTo("system:user:query"), "孙级按钮应挂到菜单下");
        });
    }

    /// <summary>验证部门实体树递归映射并保留部门专属字段。</summary>
    [Test]
    public void DeptEntityTree_MapsRecursivelyAndKeepsDeptFields()
    {
        List<SysDept> depts =
        [
            new()
            {
                Id = Id(101), ParentId = Id(100), DeptName = "研发部", OrderNum = 1,
                Leader = "张三", Phone = "13800000000", Email = "rd@example.com", Status = CommonStatus.Enabled
            },
            new() { Id = Id(100), ParentId = Guid.Empty, DeptName = "总公司", OrderNum = 1 }
        ];

        var tree = _mapper.Map<List<DeptTreeResponse>>(SysDept.BuildTree(depts));

        var node = tree.Single(n => n.DeptName == "总公司");
        var child = node.Children.Single();
        Assert.Multiple(() =>
        {
            Assert.That(child.Id, Is.EqualTo(Id(101)));
            Assert.That(child.ParentId, Is.EqualTo(Id(100)));
            Assert.That(child.DeptName, Is.EqualTo("研发部"));
            Assert.That(child.Leader, Is.EqualTo("张三"));
            Assert.That(child.Status, Is.EqualTo(CommonStatus.Enabled));
        });
    }
}
