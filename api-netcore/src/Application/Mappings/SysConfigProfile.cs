namespace Application.Mappings;

using Application.Contracts.Configs;
using Domain.Entities;

/// <summary>参数配置模块映射：实体 → 列表；创建 / 更新请求 → 实体（内置标记创建时恒为 false）。</summary>
public sealed class SysConfigProfile : MappingProfileBase
{
    /// <summary>构造函数。</summary>
    public SysConfigProfile()
    {
        // 实体 → 视图对象
        CreateMap<SysConfig, ConfigListVO>();

        // 请求 → 实体
        CreateMap<ConfigCreateRequest, SysConfig>();
        CreateMap<ConfigUpdateRequest, SysConfig>();
    }
}
