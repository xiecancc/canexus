using Application.Contracts.Dtos.Common;
using Application.Contracts.Roles;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>角色模块映射：实体 → 列表 / 下拉 / 详情（MenuIds 由处理器组装）；创建 / 更新请求 → 实体。</summary>
public sealed class SysRoleProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysRoleProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysRole, RoleListResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysRole, RoleDropdownResponse>()
            .IncludeBase<AggregateBase, AggregateListResponse>();
        // MenuIds 由处理器经关联查询组装，映射显式忽略（AssertConfigurationIsValid 兜底口径）
        CreateMap<SysRole, RoleDetailResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>()
            .ForMember(dest => dest.MenuIds, opt => opt.Ignore());

        // 请求 → 实体（IncludeBase 挂接基类映射链）
        CreateMap<RoleCreateRequest, SysRole>(MemberList.None)
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
        CreateMap<RoleUpdateRequest, SysRole>(MemberList.None)
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
    }
}
