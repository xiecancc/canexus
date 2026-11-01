
using Application.Contracts.Common;
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
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysMenu, MenuTreeResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysMenu, MenuDetailResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();

        // 请求 → 实体（IncludeBase 挂接基类映射链）
        CreateMap<MenuCreateRequest, SysMenu>()
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
        CreateMap<MenuUpdateRequest, SysMenu>()
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
    }
}
