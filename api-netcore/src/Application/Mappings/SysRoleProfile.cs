
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
        // 实体 → 视图对象
        CreateMap<SysRole, RoleListVO>();
        CreateMap<SysRole, RoleSimpleVO>();
        CreateMap<SysRole, RoleDetailVO>();

        // 请求 → 实体
        CreateMap<RoleCreateRequest, SysRole>();
        CreateMap<RoleUpdateRequest, SysRole>();
    }
}
