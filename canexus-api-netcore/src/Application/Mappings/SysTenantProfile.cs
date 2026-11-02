using Application.Contracts.Dtos.Common;
using Application.Contracts.Tenants;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>租户模块映射：实体 → 列表 / 简项；写入请求 → 实体（TenantKey 更新时由处理器拒绝变更）。</summary>
public sealed class SysTenantProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysTenantProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysTenant, TenantPagedResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();
        CreateMap<SysTenant, TenantDropdownResponse>()
            .IncludeBase<AggregateBase, AggregateDropdownResponse>();


        // 写入请求 → 实体（仅业务字段；乐观锁版本号由处理器回填，不经载荷映射；创建 / 更新共用同一载荷）
        CreateMap<TenantInputRequest, SysTenant>(MemberList.None);
    }
}
