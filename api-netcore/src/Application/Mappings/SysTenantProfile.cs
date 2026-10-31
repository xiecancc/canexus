
using Application.Contracts.Tenants;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>租户模块映射：实体 → 列表 / 简项；创建 / 更新请求 → 实体（TenantKey 更新时由处理器拒绝变更）。</summary>
public sealed class SysTenantProfile : MappingProfileBase
{
    /// <summary>构造函数。</summary>
    public SysTenantProfile()
    {
        // 实体 → 视图对象
        CreateMap<SysTenant, TenantListVO>();
        CreateMap<SysTenant, TenantSimpleVO>();

        // 请求 → 实体
        CreateMap<TenantCreateRequest, SysTenant>();
        CreateMap<TenantUpdateRequest, SysTenant>();
    }
}
