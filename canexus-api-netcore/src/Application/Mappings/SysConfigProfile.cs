
using Application.Contracts.Common;
using Application.Contracts.Configs;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>参数配置模块映射：实体 → 列表；创建 / 更新请求 → 实体（内置标记创建时恒为 false）。</summary>
public sealed class SysConfigProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysConfigProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysConfig, ConfigListResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();

        // 请求 → 实体（IncludeBase 挂接基类映射链）
        CreateMap<ConfigCreateRequest, SysConfig>()
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
        CreateMap<ConfigUpdateRequest, SysConfig>()
            .IncludeBase<AggregateChangeRequest, AggregateBase>();
    }
}
