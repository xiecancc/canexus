using Application.Contracts.Dtos.Common;
using Application.Contracts.Roles;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>角色模块映射：实体 → 列表 / 下拉 / 详情（MenuIds 由处理器组装）；写入请求 → 实体。</summary>
public sealed class SysRoleProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysRoleProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接分页最小根映射链，补齐 Id / Version / CreatedAt / Description）
        CreateMap<SysRole, RolePagedResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysRole, RoleDropdownResponse>()
            .IncludeBase<AggregateBase, AggregateDropdownResponse>();
        // MenuIds 由处理器经关联查询组装，映射显式忽略（AssertConfigurationIsValid 兜底口径）
        CreateMap<SysRole, RoleDetailResponse>()
            .IncludeBase<AggregateBase, AggregateDetailResponse>()
            .ForMember(dest => dest.MenuIds, opt => opt.Ignore());


        // 写入请求 → 实体（仅业务字段；乐观锁版本号由处理器回填，不经载荷映射；创建 / 更新共用同一载荷）
        CreateMap<RoleInputRequest, SysRole>(MemberList.None);
    }
}
