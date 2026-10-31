
using Application.Contracts.Menus;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>菜单模块映射：实体 → 树节点 / 详情（实体树经 <see cref="Domain.Entities.SysMenu.BuildTree"/> 装配后，Children 随 AutoMapper 递归映射）；创建 / 更新请求 → 实体。</summary>
public sealed class SysMenuProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysMenuProfile()
    {
        // 实体 → 视图对象
        CreateMap<SysMenu, MenuTreeVO>();
        CreateMap<SysMenu, MenuDetailVO>();

        // 请求 → 实体
        CreateMap<MenuCreateRequest, SysMenu>();
        CreateMap<MenuUpdateRequest, SysMenu>();
    }
}
