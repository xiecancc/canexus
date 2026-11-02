using Application.Contracts.Configs;
using Application.Contracts.Dtos.Common;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;
/// <summary>参数配置模块映射：实体 → 列表；写入请求 → 实体（内置标记创建时恒为 false）。</summary>
public sealed class SysConfigProfile : Profile
{
    /// <summary>构造函数。</summary>
    public SysConfigProfile()
    {
        // 实体 → 响应模型（IncludeBase 挂接基类映射链，补齐 Id / Version / 审计字段）
        CreateMap<SysConfig, ConfigPagedResponse>()
            .IncludeBase<AggregateBase, AggregatePagedResponse>();


        // 写入请求 → 实体（仅业务字段；乐观锁版本号由处理器回填，不经载荷映射；创建 / 更新共用同一载荷）
        CreateMap<ConfigInputRequest, SysConfig>(MemberList.None);
    }
}
