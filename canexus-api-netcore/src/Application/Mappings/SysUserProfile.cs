
using Application.Contracts.Common;
using Application.Contracts.Users;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>用户模块映射：实体 → 列表 / 详情（DeptName / Roles / RoleIds 由处理器组装）；创建 / 更新请求 → 实体。</summary>
public sealed class SysUserProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysUserProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysUser, UserListResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysUser, UserDetailResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();

        // 请求 → 实体（IncludeBase 挂接基类映射链；UserName / Password 仅创建时有意义，更新映射会被同名空缺自动跳过）
        CreateMap<UserCreateRequest, SysUser>()
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
        CreateMap<UserUpdateRequest, SysUser>()
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
    }
}
