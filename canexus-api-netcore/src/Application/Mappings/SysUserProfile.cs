using Application.Contracts.Dtos.Common;
using Application.Contracts.Users;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>用户模块映射：实体 → 列表 / 详情（DeptName / Roles / RoleIds 由处理器组装）；写入请求 → 实体。</summary>
public sealed class SysUserProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysUserProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接分页最小根映射链，补齐 Id / Version / CreatedAt / Description）；
        // DeptName / Roles / RoleIds 由处理器经关联查询组装，映射显式忽略（AssertConfigurationIsValid 兜底口径）
        CreateMap<SysUser, UserPagedResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>()
            .ForMember(dest => dest.DeptName, opt => opt.Ignore())
            .ForMember(dest => dest.Roles, opt => opt.Ignore());
        CreateMap<SysUser, UserDetailResponse>()
            .IncludeBase<AggregateBase, AggregateDetailResponse>()
            .ForMember(dest => dest.RoleIds, opt => opt.Ignore());

        // 写入请求 → 实体（IncludeBase 挂接基类映射链；UserName 仅创建生效，更新侧处理器手工赋值不经映射）
        // 写入请求 → 实体（仅业务字段；乐观锁版本号由处理器回填，不经载荷映射；创建 / 更新共用同一载荷）
        CreateMap<UserInputRequest, SysUser>(MemberList.None);
    }
}
